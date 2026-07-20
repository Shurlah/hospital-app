using ImmunizationSystem.Api.Shared.Database;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Twilio.Clients;
using Twilio.Http;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Security;
using Twilio.Types;

namespace ImmunizationSystem.Api.Shared.Sms;

public sealed record SmsSendResult(
    bool Succeeded,
    string Provider,
    string Status,
    string? ProviderMessageId,
    string? ProviderResponse,
    string? FailureReason);

public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken);
}

public interface ITwilioRequestValidator
{
    Task<bool> IsValidAsync(HttpRequest request, CancellationToken cancellationToken);
}

public sealed class SmsOptions
{
    public const string LoggingProvider = "Logging";
    public const string TwilioProvider = "Twilio";

    public string Provider { get; init; } = LoggingProvider;
    public string? SenderId { get; init; }
    public string? BaseUrl { get; init; }
    public string? TwilioAccountSid { get; init; }
    public string? TwilioAuthToken { get; init; }
    public string? TwilioFromPhoneNumber { get; init; }

    public static SmsOptions FromConfiguration(IConfiguration configuration) => new()
    {
        Provider = configuration["SMS_PROVIDER"] ?? LoggingProvider,
        SenderId = configuration["SMS_SENDER_ID"],
        BaseUrl = configuration["SMS_BASE_URL"],
        TwilioAccountSid = configuration["TWILIO_ACCOUNT_SID"],
        TwilioAuthToken = configuration["TWILIO_AUTH_TOKEN"],
        TwilioFromPhoneNumber = configuration["TWILIO_FROM_PHONE_NUMBER"]
    };
}

public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger, IOptions<SmsOptions> options) : ISmsSender
{
    public Task<SmsSendResult> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        var provider = options.Value.Provider;
        logger.LogInformation("SMS provider {Provider} sending to {PhoneNumber}: {Message}", provider, phoneNumber, message);
        return Task.FromResult(new SmsSendResult(true, provider, SmsStatuses.Sent, Guid.NewGuid().ToString("N"), "Logged", null));
    }
}

public sealed class TwilioSmsSender(
    ILogger<TwilioSmsSender> logger,
    IOptions<SmsOptions> options,
    ITwilioRestClient twilioRestClient) : ISmsSender
{
    public async Task<SmsSendResult> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.TwilioFromPhoneNumber))
        {
            throw new InvalidOperationException("TWILIO_FROM_PHONE_NUMBER is required when SMS_PROVIDER=Twilio.");
        }

        var callbackUrl = BuildCallbackUrl(settings.BaseUrl);
        var resource = await MessageResource.CreateAsync(
            to: new PhoneNumber(phoneNumber),
            from: new PhoneNumber(settings.TwilioFromPhoneNumber),
            body: message,
            statusCallback: callbackUrl,
            client: twilioRestClient);

        var providerStatus = resource.Status?.ToString();
        var localStatus = TwilioStatusMapper.Map(providerStatus);
        logger.LogInformation(
            "Twilio queued SMS {MessageSid} to {PhoneNumber} with status {Status}",
            resource.Sid,
            phoneNumber,
            providerStatus ?? localStatus);

        return new SmsSendResult(
            true,
            SmsOptions.TwilioProvider,
            localStatus,
            resource.Sid,
            providerStatus,
            null);
    }

    private static Uri? BuildCallbackUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return null;
        }

        return new Uri(new Uri(baseUrl.TrimEnd('/') + "/"), "api/notifications/sms/provider-callback");
    }
}

public sealed class TwilioRequestValidatorAdapter(IOptions<SmsOptions> options) : ITwilioRequestValidator
{
    public async Task<bool> IsValidAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!string.Equals(settings.Provider, SmsOptions.TwilioProvider, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(settings.TwilioAuthToken))
        {
            return false;
        }

        var signature = request.Headers["X-Twilio-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signature))
        {
            return false;
        }

        if (!request.HasFormContentType)
        {
            return false;
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var parameters = form.ToDictionary(x => x.Key, x => x.Value.ToString(), StringComparer.Ordinal);
        var validator = new RequestValidator(settings.TwilioAuthToken);

        return validator.Validate(request.GetDisplayUrl(), parameters, signature);
    }
}

public sealed class SmsReminderWorker(IServiceScopeFactory scopeFactory, ILogger<SmsReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var sender = scope.ServiceProvider.GetRequiredService<ISmsSender>();
                var dueMessages = await db.SmsNotifications
                    .Where(x => x.Status == SmsStatuses.Pending && x.ScheduledAt <= DateTime.UtcNow)
                    .OrderBy(x => x.ScheduledAt)
                    .Take(20)
                    .ToListAsync(stoppingToken);

                foreach (var message in dueMessages)
                {
                    var attemptNumber = await db.SmsDeliveryAttempts.CountAsync(x => x.SmsNotificationId == message.Id, stoppingToken) + 1;
                    var result = await sender.SendAsync(message.PhoneNumber, message.Message, stoppingToken);
                    message.Status = result.Status;
                    message.SentAt = result.Succeeded ? DateTime.UtcNow : null;
                    message.DeliveredAt = result.Status == SmsStatuses.Delivered ? DateTime.UtcNow : null;
                    message.FailedAt = result.Status == SmsStatuses.Failed ? DateTime.UtcNow : null;
                    message.ProviderMessageId = result.ProviderMessageId;
                    message.FailureReason = result.FailureReason;
                    db.SmsDeliveryAttempts.Add(new SmsDeliveryAttempt
                    {
                        SmsNotificationId = message.Id,
                        AttemptNumber = attemptNumber,
                        Provider = result.Provider,
                        ProviderResponse = result.ProviderResponse,
                        Status = message.Status
                    });
                }

                if (dueMessages.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SMS worker failed while processing due notifications");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}

public static class TwilioStatusMapper
{
    public static string Map(string? providerStatus)
    {
        if (string.IsNullOrWhiteSpace(providerStatus))
        {
            return SmsStatuses.Sent;
        }

        return providerStatus.Trim().ToLowerInvariant() switch
        {
            "accepted" or "scheduled" or "queued" or "sending" => SmsStatuses.Queued,
            "sent" => SmsStatuses.Sent,
            "delivered" or "read" => SmsStatuses.Delivered,
            "failed" or "undelivered" or "canceled" => SmsStatuses.Failed,
            _ => SmsStatuses.Sent
        };
    }
}
