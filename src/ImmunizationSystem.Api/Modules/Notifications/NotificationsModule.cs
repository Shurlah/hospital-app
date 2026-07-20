using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using ImmunizationSystem.Api.Shared.Sms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ImmunizationSystem.Api.Modules.Notifications;

public static class NotificationsModule
{
    public static IEndpointRouteBuilder MapNotificationsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notifications").WithTags("Notifications").RequireAuthorization(AuthPolicies.CanViewReports);
        group.MapGet("/sms", async (ApplicationDbContext db, int page = 1, int pageSize = 50, CancellationToken ct = default) =>
        {
            var query = db.SmsNotifications.OrderByDescending(x => x.CreatedAt);
            var total = await query.CountAsync(ct);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Results.Ok(new { items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
        });
        group.MapPost("/sms/send-test", async (TestSmsRequest request, ISmsSender sender, CancellationToken ct) =>
            Results.Ok(await sender.SendAsync(request.PhoneNumber, request.Message, ct)));
        group.MapPost("/sms/provider-callback", async (
            HttpRequest request,
            ApplicationDbContext db,
            ITwilioRequestValidator twilioRequestValidator,
            IOptions<SmsOptions> smsOptions,
            CancellationToken ct) =>
        {
            if (string.Equals(smsOptions.Value.Provider, SmsOptions.TwilioProvider, StringComparison.OrdinalIgnoreCase))
            {
                var valid = await twilioRequestValidator.IsValidAsync(request, ct);
                if (!valid)
                {
                    return Results.Unauthorized();
                }
            }

            var form = await request.ReadFormAsync(ct);
            var messageSid = form["MessageSid"].ToString();
            var providerStatus = form["MessageStatus"].ToString();
            var errorCode = form["ErrorCode"].ToString();
            var errorMessage = form["ErrorMessage"].ToString();

            if (string.IsNullOrWhiteSpace(messageSid))
            {
                return Results.BadRequest(new { error = "Missing MessageSid." });
            }

            var notification = await db.SmsNotifications.SingleOrDefaultAsync(x => x.ProviderMessageId == messageSid, ct);
            if (notification is null) return Results.NotFound();

            var status = TwilioStatusMapper.Map(providerStatus);
            notification.Status = status;
            notification.DeliveredAt = status == SmsStatuses.Delivered ? DateTime.UtcNow : notification.DeliveredAt;
            notification.FailedAt = status == SmsStatuses.Failed ? DateTime.UtcNow : notification.FailedAt;
            notification.FailureReason = string.Join(": ", new[] { errorCode, errorMessage }.Where(x => !string.IsNullOrWhiteSpace(x)));
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AllowAnonymous();
        return app;
    }
}

public sealed record TestSmsRequest(string PhoneNumber, string Message);
