using ImmunizationSystem.Api.Modules.Appointments;
using ImmunizationSystem.Api.Shared.Database;

namespace ImmunizationSystem.Api.Modules.Children;

public static class ChildSchedulePlanner
{
    public static IReadOnlyList<ChildDueVaccineItem> BuildDueVaccines(
        Child child,
        IEnumerable<VaccineSchedule> schedules,
        IEnumerable<ImmunizationRecord> immunizationRecords,
        IEnumerable<Appointment> appointments,
        DateOnly today)
    {
        var recordLookup = immunizationRecords
            .GroupBy(x => (x.VaccineId, x.DoseName), StringTupleComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Any());

        var appointmentLookup = appointments
            .GroupBy(x => (x.VaccineId, x.DoseName), StringTupleComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(x => x.AppointmentDate)
                    .ThenByDescending(x => x.AppointmentTime)
                    .ToList());

        return schedules
            .OrderBy(x => x.RecommendedAgeInWeeks)
            .ThenBy(x => x.Sequence)
            .Select(schedule =>
            {
                var key = (schedule.VaccineId, schedule.DoseName);
                var isCompleted = recordLookup.TryGetValue(key, out var recorded) && recorded;
                appointmentLookup.TryGetValue(key, out var matchingAppointments);

                var latestAppointment = matchingAppointments?.FirstOrDefault();
                var scheduledAppointment = matchingAppointments?.FirstOrDefault(x => x.Status == AppointmentStatuses.Scheduled);
                var dueDate = child.DateOfBirth.AddDays(schedule.RecommendedAgeInWeeks * 7);
                var status = ResolveStatus(isCompleted, scheduledAppointment, dueDate, today);

                return new ChildDueVaccineItem(
                    schedule.VaccineId,
                    schedule.Vaccine?.Name ?? schedule.VaccineId.ToString(),
                    schedule.DoseName,
                    schedule.RecommendedAgeInWeeks,
                    schedule.MinimumAgeInWeeks,
                    schedule.MaximumAgeInWeeks,
                    dueDate,
                    status,
                    dueDate < today && !isCompleted && scheduledAppointment is null,
                    isCompleted,
                    scheduledAppointment is not null,
                    scheduledAppointment?.Id,
                    scheduledAppointment?.AppointmentDate,
                    latestAppointment?.Status);
            })
            .ToList();
    }

    public static IReadOnlyList<Appointment> BuildAppointmentsForDueVaccines(
        Child child,
        IEnumerable<ChildDueVaccineItem> dueVaccines,
        DateOnly throughDate)
    {
        var today = AppointmentNotificationScheduler.GetSchedulingToday();

        return dueVaccines
            .Where(x => !x.IsCompleted && x.DueDate <= throughDate && !x.HasScheduledAppointment)
            .Select(item => new Appointment
            {
                ChildId = child.Id,
                VaccineId = item.VaccineId,
                DoseName = item.DoseName,
                FacilityId = child.FacilityId,
                AppointmentDate = item.DueDate < today ? today : item.DueDate
            })
            .ToList();
    }

    private static string ResolveStatus(bool isCompleted, Appointment? scheduledAppointment, DateOnly dueDate, DateOnly today)
    {
        if (isCompleted) return "Completed";
        if (scheduledAppointment is not null) return "Scheduled";
        if (dueDate < today) return "Overdue";
        if (dueDate == today) return "DueToday";
        return "Upcoming";
    }

    private sealed class StringTupleComparer : IEqualityComparer<(Guid VaccineId, string DoseName)>
    {
        public static readonly StringTupleComparer OrdinalIgnoreCase = new();

        public bool Equals((Guid VaccineId, string DoseName) x, (Guid VaccineId, string DoseName) y)
            => x.VaccineId == y.VaccineId && string.Equals(x.DoseName, y.DoseName, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((Guid VaccineId, string DoseName) obj)
            => HashCode.Combine(obj.VaccineId, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DoseName));
    }
}

public sealed record ChildDueVaccineItem(
    Guid VaccineId,
    string VaccineName,
    string DoseName,
    int RecommendedAgeInWeeks,
    int? MinimumAgeInWeeks,
    int? MaximumAgeInWeeks,
    DateOnly DueDate,
    string Status,
    bool IsOverdue,
    bool IsCompleted,
    bool HasScheduledAppointment,
    Guid? ScheduledAppointmentId,
    DateOnly? ScheduledAppointmentDate,
    string? LatestAppointmentStatus);
