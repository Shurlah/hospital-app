using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.AuditLogs;

public static class AuditLogsModule
{
    public static IEndpointRouteBuilder MapAuditLogsModule(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/audit-logs", async (Guid? userId, string? action, string? entityType, DateOnly? from, DateOnly? to, ApplicationDbContext db, CancellationToken ct) =>
        {
            var query = db.AuditLogs.AsQueryable();
            if (userId.HasValue) query = query.Where(x => x.UserId == userId);
            if (!string.IsNullOrWhiteSpace(action)) query = query.Where(x => x.Action.Contains(action));
            if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(x => x.EntityType == entityType);
            if (from.HasValue) query = query.Where(x => x.CreatedAt >= from.Value.ToDateTime(TimeOnly.MinValue));
            if (to.HasValue) query = query.Where(x => x.CreatedAt <= to.Value.ToDateTime(TimeOnly.MaxValue));
            return Results.Ok(await query.OrderByDescending(x => x.CreatedAt).Take(500).ToListAsync(ct));
        }).WithTags("AuditLogs").RequireAuthorization(AuthPolicies.CanViewReports);
        return app;
    }
}
