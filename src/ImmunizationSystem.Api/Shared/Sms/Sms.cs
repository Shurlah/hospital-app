using ImmunizationSystem.Api.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Shared.Sms;

public sealed record SmsSendResult(bool Succeeded, string? ProviderMessageId, string? ProviderResponse, string? FailureReason);

public interface ISmsSender
{
    Task<SmsSendResult> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken);
}

public sealed class LoggingSmsSender(ILogger<LoggingSmsSender> logger, IConfiguration configuration) : ISmsSender
{
    public Task<SmsSendResult> SendAsync(string phoneNumber, string message, CancellationToken cancellationToken)
    {
        var provider = configuration["SMS_PROVIDER"] ?? "Logging";
        logger.LogInformation("SMS provider {Provider} sending to {PhoneNumber}: {Message}", provider, phoneNumber, message);
        return Task.FromResult(new SmsSendResult(true, Guid.NewGuid().ToString("N"), "Logged", null));
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
                    message.Status = result.Succeeded ? SmsStatuses.Sent : SmsStatuses.Failed;
                    message.SentAt = result.Succeeded ? DateTime.UtcNow : null;
                    message.FailedAt = result.Succeeded ? null : DateTime.UtcNow;
                    message.ProviderMessageId = result.ProviderMessageId;
                    message.FailureReason = result.FailureReason;
                    db.SmsDeliveryAttempts.Add(new SmsDeliveryAttempt
                    {
                        SmsNotificationId = message.Id,
                        AttemptNumber = attemptNumber,
                        Provider = "ConfiguredProvider",
                        ProviderResponse = result.ProviderResponse,
                        Status = message.Status
                    });
                }

                if (dueMessages.Count > 0) await db.SaveChangesAsync(stoppingToken);
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
