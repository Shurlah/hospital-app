using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using ImmunizationSystem.Api.Shared.Sms;
using Microsoft.EntityFrameworkCore;

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
        group.MapPost("/sms/provider-callback", async (SmsCallbackRequest request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var notification = await db.SmsNotifications.SingleOrDefaultAsync(x => x.ProviderMessageId == request.ProviderMessageId, ct);
            if (notification is null) return Results.NotFound();
            notification.Status = request.Status;
            notification.DeliveredAt = request.Status == SmsStatuses.Delivered ? DateTime.UtcNow : notification.DeliveredAt;
            notification.FailedAt = request.Status == SmsStatuses.Failed ? DateTime.UtcNow : notification.FailedAt;
            notification.FailureReason = request.FailureReason;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).AllowAnonymous();
        return app;
    }
}

public sealed record TestSmsRequest(string PhoneNumber, string Message);

public sealed record SmsCallbackRequest(string ProviderMessageId, string Status, string? FailureReason);
