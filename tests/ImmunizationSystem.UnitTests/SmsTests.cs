using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Sms;

namespace ImmunizationSystem.UnitTests;

public sealed class SmsTests
{
    [Theory]
    [InlineData("accepted", SmsStatuses.Queued)]
    [InlineData("queued", SmsStatuses.Queued)]
    [InlineData("sending", SmsStatuses.Queued)]
    [InlineData("sent", SmsStatuses.Sent)]
    [InlineData("delivered", SmsStatuses.Delivered)]
    [InlineData("failed", SmsStatuses.Failed)]
    [InlineData("undelivered", SmsStatuses.Failed)]
    public void TwilioStatusMapper_Maps_Provider_Statuses(string providerStatus, string expectedStatus)
    {
        var actualStatus = TwilioStatusMapper.Map(providerStatus);

        Assert.Equal(expectedStatus, actualStatus);
    }

    [Fact]
    public void Appointment_Defaults_To_9am_Local_Time()
    {
        var appointment = new Appointment
        {
            ChildId = Guid.NewGuid(),
            VaccineId = Guid.NewGuid(),
            DoseName = "Penta 1",
            FacilityId = Guid.NewGuid(),
            AppointmentDate = new DateOnly(2026, 7, 15)
        };

        Assert.Equal(new TimeOnly(9, 0), appointment.AppointmentTime);
    }
}
