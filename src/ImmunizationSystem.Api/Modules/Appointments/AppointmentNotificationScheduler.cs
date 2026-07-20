using System.Globalization;
using ImmunizationSystem.Api.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Appointments;

public static class AppointmentNotificationScheduler
{
    private const string UpcomingAppointmentReminder = "UpcomingAppointmentReminder";
    private const string MissedAppointmentFollowUp = "MissedAppointmentFollowUp";
    private static readonly TimeZoneInfo SchedulingTimeZone = ResolveSchedulingTimeZone();

    public static DateOnly GetSchedulingToday()
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, SchedulingTimeZone));

    public static async Task ScheduleReminderAsync(ApplicationDbContext db, Appointment appointment, CancellationToken ct)
    {
        var child = await db.Children.Include(x => x.Guardian).SingleOrDefaultAsync(x => x.Id == appointment.ChildId, ct);
        if (child?.Guardian is null) return;

        var facilityName = await db.Facilities
            .Where(x => x.Id == appointment.FacilityId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(ct);

        var appointmentUtc = GetAppointmentUtcTimestamp(appointment);
        var nowUtc = DateTime.UtcNow;
        if (appointmentUtc <= nowUtc) return;

        var scheduledAt = appointmentUtc.AddHours(-48);
        if (scheduledAt < nowUtc)
        {
            scheduledAt = nowUtc;
        }

        db.SmsNotifications.Add(new SmsNotification
        {
            AppointmentId = appointment.Id,
            ChildId = child.Id,
            GuardianId = child.GuardianId,
            PhoneNumber = child.Guardian.PhoneNumber,
            NotificationType = UpcomingAppointmentReminder,
            Message = $"Dear Parent/Guardian, your child {child.FirstName} is due for immunization at {facilityName ?? "the facility"} on {appointment.AppointmentDate:yyyy-MM-dd} by {appointment.AppointmentTime.ToString("HH:mm", CultureInfo.InvariantCulture)}. Please attend on time.",
            ScheduledAt = scheduledAt
        });
    }

    public static async Task ScheduleMissedFollowUpAsync(ApplicationDbContext db, Appointment appointment, CancellationToken ct)
    {
        var child = await db.Children.Include(x => x.Guardian).SingleOrDefaultAsync(x => x.Id == appointment.ChildId, ct);
        if (child?.Guardian is null) return;

        var facilityName = await db.Facilities
            .Where(x => x.Id == appointment.FacilityId)
            .Select(x => x.Name)
            .SingleOrDefaultAsync(ct);

        db.SmsNotifications.Add(new SmsNotification
        {
            AppointmentId = appointment.Id,
            ChildId = child.Id,
            GuardianId = child.GuardianId,
            PhoneNumber = child.Guardian.PhoneNumber,
            NotificationType = MissedAppointmentFollowUp,
            Message = $"Dear Parent/Guardian, {child.FirstName} missed an immunization appointment at {facilityName ?? "the facility"}. Please visit the facility as soon as possible.",
            ScheduledAt = DateTime.UtcNow
        });
    }

    public static DateTime GetAppointmentUtcTimestamp(Appointment appointment)
    {
        var localAppointment = appointment.AppointmentDate.ToDateTime(appointment.AppointmentTime, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localAppointment, SchedulingTimeZone);
    }

    private static TimeZoneInfo ResolveSchedulingTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Lagos");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("W. Central Africa Standard Time");
        }
    }
}
