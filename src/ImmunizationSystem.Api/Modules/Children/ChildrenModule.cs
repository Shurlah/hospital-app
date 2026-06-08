using ImmunizationSystem.Api.Shared.Cqrs;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Children;

public static class ChildrenModule
{
    public static IEndpointRouteBuilder MapChildrenModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/children").WithTags("Children").RequireAuthorization();
        group.MapPost("/", async (RegisterChildCommand command, IRequestDispatcher dispatcher, CancellationToken ct) =>
        {
            var child = await dispatcher.SendAsync(command, ct);
            return Results.Created($"/api/children/{child.Id}", child);
        }).RequireAuthorization(AuthPolicies.CanRecordImmunization);
        group.MapGet("/", async (ApplicationDbContext db, Guid? facilityId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        {
            var query = db.Children.Include(x => x.Guardian).AsQueryable();
            if (facilityId.HasValue) query = query.Where(x => x.FacilityId == facilityId);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Results.Ok(new { items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
        });
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
            await db.Children.Include(x => x.Guardian).SingleOrDefaultAsync(x => x.Id == id, ct) is { } child ? Results.Ok(child) : Results.NotFound());
        group.MapGet("/search", async (string? q, string? phone, Guid? facilityId, ApplicationDbContext db, CancellationToken ct) =>
        {
            var query = db.Children.Include(x => x.Guardian).AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => (x.FirstName + " " + x.LastName).ToLower().Contains(q.ToLower()));
            if (!string.IsNullOrWhiteSpace(phone)) query = query.Where(x => x.Guardian!.PhoneNumber.Contains(phone));
            if (facilityId.HasValue) query = query.Where(x => x.FacilityId == facilityId);
            return Results.Ok(await query.Take(50).ToListAsync(ct));
        });
        group.MapGet("/duplicates", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Children.Include(x => x.Guardian).Where(x => x.IsPossibleDuplicate).ToListAsync(ct)));
        return app;
    }
}

public sealed record RegisterChildCommand(
    Guid? Id,
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly DateOfBirth,
    string Sex,
    Guid GuardianId,
    Guid FacilityId,
    Guid CreatedByUserId,
    Guid? CreatedByDeviceId) : ICommand<Child>;

public sealed class RegisterChildHandler(ApplicationDbContext dbContext) : ICommandHandler<RegisterChildCommand, Child>
{
    public async Task<Child> HandleAsync(RegisterChildCommand command, CancellationToken cancellationToken)
    {
        var guardian = await dbContext.Guardians.FindAsync([command.GuardianId], cancellationToken);
        var possibleDuplicate = guardian is not null && await dbContext.Children.Include(x => x.Guardian).AnyAsync(x =>
            x.FirstName == command.FirstName &&
            x.LastName == command.LastName &&
            x.DateOfBirth == command.DateOfBirth &&
            x.Sex == command.Sex &&
            x.FacilityId == command.FacilityId &&
            x.Guardian!.PhoneNumber == guardian.PhoneNumber,
            cancellationToken);

        var child = new Child
        {
            Id = command.Id ?? Guid.NewGuid(),
            FirstName = command.FirstName,
            MiddleName = command.MiddleName,
            LastName = command.LastName,
            DateOfBirth = command.DateOfBirth,
            Sex = command.Sex,
            GuardianId = command.GuardianId,
            FacilityId = command.FacilityId,
            CreatedByUserId = command.CreatedByUserId,
            CreatedByDeviceId = command.CreatedByDeviceId,
            IsPossibleDuplicate = possibleDuplicate
        };
        dbContext.Children.Add(child);
        dbContext.AuditLogs.Add(new AuditLog { UserId = command.CreatedByUserId, DeviceId = command.CreatedByDeviceId, Action = "Child registered", EntityType = "Child", EntityId = child.Id });
        dbContext.ServerChangeLogs.Add(new ServerChangeLog
        {
            EntityType = "Child",
            EntityId = child.Id,
            OperationType = "Create",
            FacilityId = child.FacilityId,
            CreatedByUserId = child.CreatedByUserId,
            PayloadJson = ApplicationDbContext.ToJsonElement(child)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return child;
    }
}
