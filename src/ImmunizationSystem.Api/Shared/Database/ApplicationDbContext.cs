using System.Text.Json;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Shared.Database;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Facility> Facilities => Set<Facility>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Child> Children => Set<Child>();
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<Vaccine> Vaccines => Set<Vaccine>();
    public DbSet<VaccineSchedule> VaccineSchedules => Set<VaccineSchedule>();
    public DbSet<ImmunizationRecord> ImmunizationRecords => Set<ImmunizationRecord>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<SyncInbox> SyncInbox => Set<SyncInbox>();
    public DbSet<ServerChangeLog> ServerChangeLogs => Set<ServerChangeLog>();
    public DbSet<SmsNotification> SmsNotifications => Set<SmsNotification>();
    public DbSet<SmsDeliveryAttempt> SmsDeliveryAttempts => Set<SmsDeliveryAttempt>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>().HasIndex(x => x.Name).IsUnique();
        modelBuilder.Entity<User>().HasIndex(x => x.Email).IsUnique();
        modelBuilder.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<Facility>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Device>().HasIndex(x => x.DeviceIdentifier).IsUnique();
        modelBuilder.Entity<Child>().HasIndex(x => x.FacilityId);
        modelBuilder.Entity<Child>().HasIndex(x => x.GuardianId);
        modelBuilder.Entity<Guardian>().HasIndex(x => x.PhoneNumber);
        modelBuilder.Entity<Vaccine>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<ImmunizationRecord>().HasIndex(x => new { x.ChildId, x.VaccineId, x.DoseName });
        modelBuilder.Entity<Appointment>().HasIndex(x => x.AppointmentDate);
        modelBuilder.Entity<Appointment>().HasIndex(x => x.Status);
        modelBuilder.Entity<SyncInbox>().HasIndex(x => new { x.ClientChangeId, x.DeviceId }).IsUnique();
        modelBuilder.Entity<ServerChangeLog>().HasKey(x => x.ChangeVersion);
        modelBuilder.Entity<ServerChangeLog>().Property(x => x.ChangeVersion).ValueGeneratedOnAdd();
        modelBuilder.Entity<ServerChangeLog>().Property(x => x.PayloadJson).HasColumnType("jsonb");
        modelBuilder.Entity<SmsNotification>().HasIndex(x => x.Status);
        modelBuilder.Entity<AuditLog>().HasIndex(x => x.CreatedAt);

        modelBuilder.Entity<Role>().HasData(
            new Role { Id = SeedIds.SystemAdministratorRole, Name = RoleNames.SystemAdministrator, Description = "Full system administration", CreatedAt = SeedIds.SeedTime },
            new Role { Id = SeedIds.LgaOfficialRole, Name = RoleNames.LgaHealthOfficial, Description = "LGA reporting and oversight", CreatedAt = SeedIds.SeedTime },
            new Role { Id = SeedIds.FacilitySupervisorRole, Name = RoleNames.FacilitySupervisor, Description = "Facility-level supervision", CreatedAt = SeedIds.SeedTime },
            new Role { Id = SeedIds.HealthWorkerRole, Name = RoleNames.HealthWorker, Description = "Offline capture and immunization workflows", CreatedAt = SeedIds.SeedTime },
            new Role { Id = SeedIds.AuditorRole, Name = RoleNames.Auditor, Description = "Read-only audit access", CreatedAt = SeedIds.SeedTime });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.Version++;
            }
        }

        return base.SaveChangesAsync(cancellationToken);
    }

    public static JsonElement ToJsonElement<T>(T value)
        => JsonSerializer.SerializeToElement(value, JsonOptions.Default);
}

public static class SeedIds
{
    public static readonly DateTime SeedTime = new(2026, 5, 24, 0, 0, 0, DateTimeKind.Utc);
    public static readonly Guid SuperAdministratorUser = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    public static readonly Guid SystemAdministratorRole = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    public static readonly Guid LgaOfficialRole = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    public static readonly Guid FacilitySupervisorRole = Guid.Parse("a3333333-3333-3333-3333-333333333333");
    public static readonly Guid HealthWorkerRole = Guid.Parse("a4444444-4444-4444-4444-444444444444");
    public static readonly Guid AuditorRole = Guid.Parse("a5555555-5555-5555-5555-555555555555");
}

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}
