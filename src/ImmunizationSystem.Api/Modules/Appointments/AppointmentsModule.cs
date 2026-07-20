using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Appointments;

public static class AppointmentsModule
{
    public static IEndpointRouteBuilder MapAppointmentsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/appointments").WithTags("Appointments").RequireAuthorization();
        group.MapPost("/", async (Appointment request, ApplicationDbContext db, CancellationToken ct) =>
        {
            db.Appointments.Add(request);
            db.AuditLogs.Add(new AuditLog { Action = "Appointment created", EntityType = "Appointment", EntityId = request.Id });
            await AppointmentNotificationScheduler.ScheduleReminderAsync(db, request, ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/appointments/{request.Id}", request);
        }).RequireAuthorization(AuthPolicies.CanRecordImmunization);
        group.MapGet("/", async (ApplicationDbContext db, Guid? facilityId, DateOnly? from, DateOnly? to, CancellationToken ct) =>
        {
            var query = db.Appointments.AsQueryable();
            if (facilityId.HasValue) query = query.Where(x => x.FacilityId == facilityId);
            if (from.HasValue) query = query.Where(x => x.AppointmentDate >= from);
            if (to.HasValue) query = query.Where(x => x.AppointmentDate <= to);
            return Results.Ok(await query.OrderBy(x => x.AppointmentDate).ThenBy(x => x.AppointmentTime).Take(200).ToListAsync(ct));
        });
        group.MapGet("/upcoming", async (ApplicationDbContext db, Guid? facilityId, CancellationToken ct) =>
        {
            var today = AppointmentNotificationScheduler.GetSchedulingToday();
            var week = today.AddDays(7);
            var query = db.Appointments.Where(x => x.Status == AppointmentStatuses.Scheduled && x.AppointmentDate >= today && x.AppointmentDate <= week);
            if (facilityId.HasValue) query = query.Where(x => x.FacilityId == facilityId);
            return Results.Ok(await query.OrderBy(x => x.AppointmentDate).ThenBy(x => x.AppointmentTime).ToListAsync(ct));
        });
        group.MapPost("/{id:guid}/complete", async (Guid id, CompleteAppointmentRequest request, ApplicationDbContext db, CancellationToken ct) =>
            await UpdateStatusAsync(id, AppointmentStatuses.Completed, request.CompletedAt ?? DateTime.UtcNow, db, ct));
        group.MapPost("/{id:guid}/mark-missed", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
            await UpdateStatusAsync(id, AppointmentStatuses.Missed, DateTime.UtcNow, db, ct));
        return app;
    }

    private static async Task<IResult> UpdateStatusAsync(Guid id, string status, DateTime timestamp, ApplicationDbContext db, CancellationToken ct)
    {
        var appointment = await db.Appointments.FindAsync([id], ct);
        if (appointment is null) return Results.NotFound();
        if (appointment.Status is AppointmentStatuses.Completed or AppointmentStatuses.Cancelled) return Results.Conflict();
        appointment.Status = status;
        if (status == AppointmentStatuses.Completed) appointment.CompletedAt = timestamp;
        if (status == AppointmentStatuses.Missed)
        {
            appointment.MissedAt = timestamp;
            await AppointmentNotificationScheduler.ScheduleMissedFollowUpAsync(db, appointment, ct);
        }
        db.AuditLogs.Add(new AuditLog { Action = $"Appointment {status}", EntityType = "Appointment", EntityId = appointment.Id });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}

public sealed record CompleteAppointmentRequest(DateTime? CompletedAt);
