using ImmunizationSystem.Api.Shared.Database;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Guardians;

public static class GuardiansModule
{
    public static IEndpointRouteBuilder MapGuardiansModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guardians").WithTags("Guardians").RequireAuthorization();
        group.MapPost("/", async (Guardian request, ApplicationDbContext db, CancellationToken ct) =>
        {
            db.Guardians.Add(request);
            db.AuditLogs.Add(new AuditLog { Action = "Guardian created", EntityType = "Guardian", EntityId = request.Id });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/guardians/{request.Id}", request);
        });
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
            await db.Guardians.FindAsync([id], ct) is { } guardian ? Results.Ok(guardian) : Results.NotFound());
        group.MapPut("/{id:guid}", async (Guid id, Guardian request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var guardian = await db.Guardians.FindAsync([id], ct);
            if (guardian is null) return Results.NotFound();
            guardian.FullName = request.FullName;
            guardian.PhoneNumber = request.PhoneNumber;
            guardian.AlternativePhoneNumber = request.AlternativePhoneNumber;
            guardian.RelationshipToChild = request.RelationshipToChild;
            guardian.Address = request.Address;
            guardian.Ward = request.Ward;
            db.AuditLogs.Add(new AuditLog { Action = "Guardian updated", EntityType = "Guardian", EntityId = guardian.Id });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        return app;
    }
}
