using ImmunizationSystem.Api.Modules.Children;
using ImmunizationSystem.Api.Shared.Database;

namespace ImmunizationSystem.UnitTests;

public sealed class ChildSchedulePlannerTests
{
    [Fact]
    public void BuildDueVaccines_Marks_Completed_Scheduled_And_Upcoming_Doses()
    {
        var vaccineId = Guid.NewGuid();
        var child = new Child
        {
            Id = Guid.NewGuid(),
            FirstName = "Ada",
            LastName = "Musa",
            DateOfBirth = new DateOnly(2026, 1, 1),
            Sex = "Female",
            GuardianId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid()
        };

        var schedules = new[]
        {
            new VaccineSchedule { VaccineId = vaccineId, Vaccine = new Vaccine { Id = vaccineId, Name = "BCG", Code = "BCG" }, DoseName = "BCG 1", RecommendedAgeInWeeks = 0, Sequence = 1 },
            new VaccineSchedule { VaccineId = vaccineId, Vaccine = new Vaccine { Id = vaccineId, Name = "BCG", Code = "BCG" }, DoseName = "BCG 2", RecommendedAgeInWeeks = 6, Sequence = 2 },
            new VaccineSchedule { VaccineId = vaccineId, Vaccine = new Vaccine { Id = vaccineId, Name = "BCG", Code = "BCG" }, DoseName = "BCG 3", RecommendedAgeInWeeks = 10, Sequence = 3 }
        };

        var records = new[]
        {
            new ImmunizationRecord
            {
                ChildId = child.Id,
                VaccineId = vaccineId,
                DoseName = "BCG 1",
                DateAdministered = new DateOnly(2026, 1, 1),
                FacilityId = child.FacilityId,
                AdministeredByUserId = child.CreatedByUserId
            }
        };

        var appointments = new[]
        {
            new Appointment
            {
                ChildId = child.Id,
                VaccineId = vaccineId,
                DoseName = "BCG 2",
                FacilityId = child.FacilityId,
                AppointmentDate = new DateOnly(2026, 2, 12),
                Status = AppointmentStatuses.Scheduled
            }
        };

        var result = ChildSchedulePlanner.BuildDueVaccines(child, schedules, records, appointments, new DateOnly(2026, 2, 1));

        Assert.Collection(
            result,
            first =>
            {
                Assert.Equal("BCG 1", first.DoseName);
                Assert.Equal("Completed", first.Status);
                Assert.True(first.IsCompleted);
            },
            second =>
            {
                Assert.Equal("BCG 2", second.DoseName);
                Assert.Equal("Scheduled", second.Status);
                Assert.True(second.HasScheduledAppointment);
            },
            third =>
            {
                Assert.Equal("BCG 3", third.DoseName);
                Assert.Equal("Upcoming", third.Status);
                Assert.False(third.HasScheduledAppointment);
            });
    }

    [Fact]
    public void BuildAppointmentsForDueVaccines_Creates_Only_Incomplete_Unscheduled_Doses_Through_Horizon()
    {
        var child = new Child
        {
            Id = Guid.NewGuid(),
            FirstName = "Kemi",
            LastName = "Ayo",
            DateOfBirth = new DateOnly(2026, 1, 1),
            Sex = "Female",
            GuardianId = Guid.NewGuid(),
            FacilityId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid()
        };

        var dueVaccines = new[]
        {
            new ChildDueVaccineItem(Guid.NewGuid(), "BCG", "BCG 1", 0, null, null, new DateOnly(2026, 1, 1), "Overdue", true, false, false, null, null, null),
            new ChildDueVaccineItem(Guid.NewGuid(), "Penta", "Penta 1", 6, null, null, new DateOnly(2026, 2, 12), "Scheduled", false, false, true, Guid.NewGuid(), new DateOnly(2026, 2, 12), AppointmentStatuses.Scheduled),
            new ChildDueVaccineItem(Guid.NewGuid(), "Penta", "Penta 2", 10, null, null, new DateOnly(2026, 3, 12), "Upcoming", false, false, false, null, null, null),
            new ChildDueVaccineItem(Guid.NewGuid(), "Measles", "Measles 1", 39, null, null, new DateOnly(2026, 10, 1), "Upcoming", false, false, false, null, null, null)
        };

        var created = ChildSchedulePlanner.BuildAppointmentsForDueVaccines(child, dueVaccines, new DateOnly(2026, 4, 1));

        Assert.Equal(2, created.Count);
        Assert.All(created, appointment => Assert.Equal(child.FacilityId, appointment.FacilityId));
        Assert.Contains(created, appointment => appointment.DoseName == "BCG 1");
        Assert.Contains(created, appointment => appointment.DoseName == "Penta 2");
        Assert.DoesNotContain(created, appointment => appointment.DoseName == "Penta 1");
        Assert.DoesNotContain(created, appointment => appointment.DoseName == "Measles 1");
    }
}
