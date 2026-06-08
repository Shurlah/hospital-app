using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Reports;

public static class ReportsModule
{
    public static IEndpointRouteBuilder MapReportsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization(AuthPolicies.CanViewReports);
        group.MapGet("/immunization-coverage", async (Guid? facilityId, DateOnly? from, DateOnly? to, ApplicationDbContext db, CancellationToken ct) =>
        {
            var children = db.Children.AsQueryable();
            var records = db.ImmunizationRecords.AsQueryable();
            if (facilityId.HasValue)
            {
                children = children.Where(x => x.FacilityId == facilityId);
                records = records.Where(x => x.FacilityId == facilityId);
            }
            if (from.HasValue) records = records.Where(x => x.DateAdministered >= from);
            if (to.HasValue) records = records.Where(x => x.DateAdministered <= to);
            return Results.Ok(new
            {
                registeredChildren = await children.CountAsync(ct),
                completedImmunizations = await records.CountAsync(ct),
                missedAppointments = await db.Appointments.CountAsync(x => x.Status == AppointmentStatuses.Missed && (!facilityId.HasValue || x.FacilityId == facilityId), ct)
            });
        });
        group.MapGet("/missed-appointments", async (Guid? facilityId, ApplicationDbContext db, CancellationToken ct) =>
        {
            var query = db.Appointments.Where(x => x.Status == AppointmentStatuses.Missed);
            if (facilityId.HasValue) query = query.Where(x => x.FacilityId == facilityId);
            return Results.Ok(await query.OrderByDescending(x => x.MissedAt).Take(200).ToListAsync(ct));
        });
        group.MapGet("/sms-delivery", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(new
            {
                sent = await db.SmsNotifications.CountAsync(x => x.Status == SmsStatuses.Sent, ct),
                delivered = await db.SmsNotifications.CountAsync(x => x.Status == SmsStatuses.Delivered, ct),
                failed = await db.SmsNotifications.CountAsync(x => x.Status == SmsStatuses.Failed, ct)
            }));
        group.MapGet("/sync-reliability", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(new
            {
                total = await db.SyncInbox.CountAsync(ct),
                accepted = await db.SyncInbox.CountAsync(x => x.Status == "Accepted", ct),
                failed = await db.SyncInbox.CountAsync(x => x.Status == "Failed", ct),
                conflict = await db.SyncInbox.CountAsync(x => x.Status == "Conflict", ct)
            }));
        group.MapGet("/facility-performance", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Facilities.Select(f => new
            {
                facilityId = f.Id,
                f.Name,
                children = db.Children.Count(c => c.FacilityId == f.Id),
                immunizations = db.ImmunizationRecords.Count(i => i.FacilityId == f.Id),
                missedAppointments = db.Appointments.Count(a => a.FacilityId == f.Id && a.Status == AppointmentStatuses.Missed)
            }).ToListAsync(ct)));
        return app;
    }
}
