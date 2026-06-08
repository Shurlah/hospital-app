using System.Net;
using ImmunizationSystem.Api.Shared.Cqrs;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Errors;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Facilities;

public static class FacilitiesModule
{
    public static IEndpointRouteBuilder MapFacilitiesModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/facilities").WithTags("Facilities").RequireAuthorization();
        group.MapPost("/", async (CreateFacilityCommand command, IRequestDispatcher dispatcher, CancellationToken ct) =>
        {
            var facility = await dispatcher.SendAsync(command, ct);
            return Results.Created($"/api/facilities/{facility.Id}", facility);
        }).RequireAuthorization(AuthPolicies.CanManageFacilities);
        group.MapGet("/", async (ApplicationDbContext db, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        {
            var query = db.Facilities.OrderBy(x => x.Name);
            var total = await query.CountAsync(ct);
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Results.Ok(new { items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
        });
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
            await db.Facilities.FindAsync([id], ct) is { } facility ? Results.Ok(facility) : Results.NotFound());
        group.MapPut("/{id:guid}", async (Guid id, UpdateFacilityRequest request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var facility = await db.Facilities.FindAsync([id], ct);
            if (facility is null) return Results.NotFound();
            if (await db.Facilities.AnyAsync(x => x.Id != id && x.Code == request.Code, ct)) return Results.Conflict();
            facility.Name = request.Name;
            facility.Code = request.Code;
            facility.Address = request.Address;
            facility.Ward = request.Ward;
            facility.Lga = request.Lga;
            facility.State = request.State;
            db.AuditLogs.Add(new AuditLog { Action = "Facility updated", EntityType = "Facility", EntityId = facility.Id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.CanManageFacilities);
        return app;
    }
}

public sealed record CreateFacilityCommand(string Name, string Code, string? Address, string? Ward, string Lga, string State)
    : ICommand<Facility>;

public sealed record UpdateFacilityRequest(string Name, string Code, string? Address, string? Ward, string Lga, string State);

public sealed class CreateFacilityHandler(ApplicationDbContext dbContext) : ICommandHandler<CreateFacilityCommand, Facility>
{
    public async Task<Facility> HandleAsync(CreateFacilityCommand command, CancellationToken cancellationToken)
    {
        if (await dbContext.Facilities.AnyAsync(x => x.Code == command.Code, cancellationToken))
            throw new ApiException("CONFLICT", "Facility code already exists.", HttpStatusCode.Conflict);
        var facility = new Facility
        {
            Name = command.Name,
            Code = command.Code,
            Address = command.Address,
            Ward = command.Ward,
            Lga = string.IsNullOrWhiteSpace(command.Lga) ? "Alimosho" : command.Lga,
            State = string.IsNullOrWhiteSpace(command.State) ? "Lagos" : command.State
        };
        dbContext.Facilities.Add(facility);
        dbContext.AuditLogs.Add(new AuditLog { Action = "Facility created", EntityType = "Facility", EntityId = facility.Id });
        await dbContext.SaveChangesAsync(cancellationToken);
        return facility;
    }
}
