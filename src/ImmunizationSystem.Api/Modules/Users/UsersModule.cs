using System.Net;
using ImmunizationSystem.Api.Shared.Cqrs;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Errors;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Users;

public static class UsersModule
{
    public static IEndpointRouteBuilder MapUsersModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization(AuthPolicies.CanManageUsers);
        group.MapPost("/", async (CreateUserCommand command, IRequestDispatcher dispatcher, CancellationToken ct) =>
        {
            var user = await dispatcher.SendAsync(command, ct);
            return Results.Created($"/api/users/{user.Id}", user);
        });
        group.MapGet("/", async (ApplicationDbContext db, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        {
            var query = db.Users.Include(x => x.Role).OrderBy(x => x.FullName);
            var total = await query.CountAsync(ct);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
                .Select(x => new UserDto(x.Id, x.FullName, x.Email, x.PhoneNumber, x.Role!.Name, x.FacilityId, x.IsActive))
                .ToListAsync(ct);
            return Results.Ok(new { items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
        });
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
            await db.Users.Include(x => x.Role).Where(x => x.Id == id)
                .Select(x => new UserDto(x.Id, x.FullName, x.Email, x.PhoneNumber, x.Role!.Name, x.FacilityId, x.IsActive))
                .SingleOrDefaultAsync(ct) is { } user ? Results.Ok(user) : Results.NotFound());
        group.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([id], ct);
            if (user is null) return Results.NotFound();
            user.FullName = request.FullName;
            user.PhoneNumber = request.PhoneNumber;
            user.RoleId = request.RoleId;
            user.FacilityId = request.FacilityId;
            db.AuditLogs.Add(new AuditLog { Action = "User updated", EntityType = "User", EntityId = user.Id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        group.MapPost("/{id:guid}/disable", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
        {
            var user = await db.Users.FindAsync([id], ct);
            if (user is null) return Results.NotFound();
            user.IsActive = false;
            await db.RefreshTokens.Where(x => x.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTime.UtcNow), ct);
            db.AuditLogs.Add(new AuditLog { Action = "User disabled", EntityType = "User", EntityId = user.Id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        return app;
    }
}

public sealed record CreateUserCommand(string FullName, string Email, string? PhoneNumber, string Password, Guid RoleId, Guid? FacilityId)
    : ICommand<UserDto>;

public sealed record UpdateUserRequest(string FullName, string? PhoneNumber, Guid RoleId, Guid? FacilityId);

public sealed record UserDto(Guid Id, string FullName, string Email, string? PhoneNumber, string Role, Guid? FacilityId, bool IsActive);

public sealed class CreateUserHandler(ApplicationDbContext dbContext, IPasswordHasher passwordHasher)
    : ICommandHandler<CreateUserCommand, UserDto>
{
    public async Task<UserDto> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.Users.AnyAsync(x => x.Email == command.Email, cancellationToken))
            throw new ApiException("CONFLICT", "Email is already registered.", HttpStatusCode.Conflict);

        var role = await dbContext.Roles.FindAsync([command.RoleId], cancellationToken)
            ?? throw new ApiException("VALIDATION_ERROR", "Invalid role.");

        var user = new User
        {
            FullName = command.FullName,
            Email = command.Email,
            PhoneNumber = command.PhoneNumber,
            PasswordHash = passwordHasher.Hash(command.Password),
            RoleId = command.RoleId,
            FacilityId = command.FacilityId
        };
        dbContext.Users.Add(user);
        dbContext.AuditLogs.Add(new AuditLog { Action = "User created", EntityType = "User", EntityId = user.Id });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new UserDto(user.Id, user.FullName, user.Email, user.PhoneNumber, role.Name, user.FacilityId, user.IsActive);
    }
}
