using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Vaccines;

public static class VaccinesModule
{
    public static IEndpointRouteBuilder MapVaccinesModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vaccines").WithTags("Vaccines").RequireAuthorization();
        group.MapPost("/", async (Vaccine request, ApplicationDbContext db, CancellationToken ct) =>
        {
            db.Vaccines.Add(request);
            db.ServerChangeLogs.Add(new ServerChangeLog { EntityType = "Vaccine", EntityId = request.Id, OperationType = "Create", PayloadJson = ApplicationDbContext.ToJsonElement(request) });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/vaccines/{request.Id}", request);
        }).RequireAuthorization(AuthPolicies.SystemAdminOnly);
        group.MapGet("/", async (ApplicationDbContext db, CancellationToken ct) => Results.Ok(await db.Vaccines.Where(x => x.IsActive).ToListAsync(ct)));
        group.MapPut("/{id:guid}", async (Guid id, Vaccine request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var vaccine = await db.Vaccines.FindAsync([id], ct);
            if (vaccine is null) return Results.NotFound();
            vaccine.Name = request.Name;
            vaccine.Code = request.Code;
            vaccine.Description = request.Description;
            db.ServerChangeLogs.Add(new ServerChangeLog { EntityType = "Vaccine", EntityId = vaccine.Id, OperationType = "Update", PayloadJson = ApplicationDbContext.ToJsonElement(vaccine) });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.SystemAdminOnly);
        group.MapPost("/{id:guid}/disable", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
        {
            var vaccine = await db.Vaccines.FindAsync([id], ct);
            if (vaccine is null) return Results.NotFound();
            vaccine.IsActive = false;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.SystemAdminOnly);
        group.MapPost("/{id:guid}/schedules", async (Guid id, VaccineSchedule request, ApplicationDbContext db, CancellationToken ct) =>
        {
            request.VaccineId = id;
            db.VaccineSchedules.Add(request);
            db.ServerChangeLogs.Add(new ServerChangeLog { EntityType = "VaccineSchedule", EntityId = request.Id, OperationType = "Create", PayloadJson = ApplicationDbContext.ToJsonElement(request) });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/vaccines/{id}/schedules/{request.Id}", request);
        }).RequireAuthorization(AuthPolicies.SystemAdminOnly);
        return app;
    }
}
