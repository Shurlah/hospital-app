using System.Text.Json;

namespace ImmunizationSystem.Api.Shared.Database;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    public long Version { get; set; } = 1;
}

public sealed class Role : Entity
{
    public required string Name { get; set; }
    public string? Description { get; set; }
}

public sealed class User : Entity
{
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public string? PhoneNumber { get; set; }
    public required string PasswordHash { get; set; }
    public Guid RoleId { get; set; }
    public Role? Role { get; set; }
    public Guid? FacilityId { get; set; }
    public Facility? Facility { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public required string TokenHash { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? UsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public sealed class Facility : Entity
{
    public required string Name { get; set; }
    public required string Code { get; set; }
    public string? Address { get; set; }
    public string? Ward { get; set; }
    public string Lga { get; set; } = "Alimosho";
    public string State { get; set; } = "Lagos";
    public bool IsActive { get; set; } = true;
}

public sealed class Device : Entity
{
    public required string DeviceIdentifier { get; set; }
    public Guid UserId { get; set; }
    public Guid FacilityId { get; set; }
    public string? DeviceName { get; set; }
    public string? Platform { get; set; }
    public bool IsApproved { get; set; }
    public DateTime? LastSeenAt { get; set; }
}

public sealed class Guardian : Entity
{
    public required string FullName { get; set; }
    public required string PhoneNumber { get; set; }
    public string? AlternativePhoneNumber { get; set; }
    public string? RelationshipToChild { get; set; }
    public string? Address { get; set; }
    public string? Ward { get; set; }
}

public sealed class Child : Entity
{
    public required string FirstName { get; set; }
    public string? MiddleName { get; set; }
    public required string LastName { get; set; }
    public DateOnly DateOfBirth { get; set; }
    public required string Sex { get; set; }
    public Guid GuardianId { get; set; }
    public Guardian? Guardian { get; set; }
    public Guid FacilityId { get; set; }
    public string RegistrationSource { get; set; } = "Api";
    public Guid CreatedByUserId { get; set; }
    public Guid? CreatedByDeviceId { get; set; }
    public bool IsPossibleDuplicate { get; set; }
}

public sealed class Vaccine : Entity
{
    public required string Name { get; set; }
    public required string Code { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class VaccineSchedule : Entity
{
    public Guid VaccineId { get; set; }
    public Vaccine? Vaccine { get; set; }
    public required string DoseName { get; set; }
    public int RecommendedAgeInWeeks { get; set; }
    public int? MinimumAgeInWeeks { get; set; }
    public int? MaximumAgeInWeeks { get; set; }
    public int Sequence { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ImmunizationRecord : Entity
{
    public Guid ChildId { get; set; }
    public Guid VaccineId { get; set; }
    public required string DoseName { get; set; }
    public DateOnly DateAdministered { get; set; }
    public Guid FacilityId { get; set; }
    public Guid AdministeredByUserId { get; set; }
    public Guid? CreatedByDeviceId { get; set; }
    public string? Notes { get; set; }
    public bool IsCorrection { get; set; }
    public Guid? CorrectedRecordId { get; set; }
}

public sealed class Appointment : Entity
{
    public Guid ChildId { get; set; }
    public Guid VaccineId { get; set; }
    public required string DoseName { get; set; }
    public Guid FacilityId { get; set; }
    public DateOnly AppointmentDate { get; set; }
    public TimeOnly AppointmentTime { get; set; } = new(9, 0);
    public string Status { get; set; } = AppointmentStatuses.Scheduled;
    public DateTime? CompletedAt { get; set; }
    public DateTime? MissedAt { get; set; }
}

public static class AppointmentStatuses
{
    public const string Scheduled = "Scheduled";
    public const string Completed = "Completed";
    public const string Missed = "Missed";
    public const string Cancelled = "Cancelled";
}

public sealed class SyncInbox : Entity
{
    public Guid ClientChangeId { get; set; }
    public Guid DeviceId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string OperationType { get; set; }
    public required string Status { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string? ResultMessage { get; set; }
}

public sealed class ServerChangeLog
{
    public long ChangeVersion { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string OperationType { get; set; }
    public JsonElement PayloadJson { get; set; }
    public Guid? FacilityId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? CreatedByUserId { get; set; }
}

public sealed class SmsNotification : Entity
{
    public Guid? AppointmentId { get; set; }
    public Guid? ChildId { get; set; }
    public Guid? GuardianId { get; set; }
    public required string PhoneNumber { get; set; }
    public required string Message { get; set; }
    public required string NotificationType { get; set; }
    public string Status { get; set; } = SmsStatuses.Pending;
    public DateTime ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? FailedAt { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class SmsDeliveryAttempt : Entity
{
    public Guid SmsNotificationId { get; set; }
    public int AttemptNumber { get; set; }
    public required string Provider { get; set; }
    public string? ProviderResponse { get; set; }
    public required string Status { get; set; }
    public DateTime AttemptedAt { get; set; } = DateTime.UtcNow;
}

public static class SmsStatuses
{
    public const string Pending = "Pending";
    public const string Queued = "Queued";
    public const string Sent = "Sent";
    public const string Delivered = "Delivered";
    public const string Failed = "Failed";
    public const string Cancelled = "Cancelled";
}

public sealed class AuditLog : Entity
{
    public Guid? UserId { get; set; }
    public Guid? DeviceId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid? EntityId { get; set; }
    public string? IpAddress { get; set; }
    public string? MetadataJson { get; set; }
}
