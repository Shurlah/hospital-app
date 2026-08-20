using System.Net;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Errors;
using ImmunizationSystem.Api.Shared.Phone;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Guardians;

public static class GuardiansModule
{
    public static IEndpointRouteBuilder MapGuardiansModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/guardians").WithTags("Guardians").RequireAuthorization();
        group.MapPost("/", async (Guardian request, ApplicationDbContext db, CancellationToken ct) =>
        {
            request.PhoneNumber = NormalizePhoneNumber(request.PhoneNumber, required: true)!;
            request.AlternativePhoneNumber = NormalizePhoneNumber(request.AlternativePhoneNumber, required: false);
            db.Guardians.Add(request);
            db.AuditLogs.Add(new AuditLog { Action = "Guardian created", EntityType = "Guardian", EntityId = request.Id });
            db.ServerChangeLogs.Add(new ServerChangeLog
            {
                EntityType = "Guardian",
                EntityId = request.Id,
                OperationType = "Create",
                PayloadJson = ApplicationDbContext.ToJsonElement(request)
            });
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
            guardian.PhoneNumber = NormalizePhoneNumber(request.PhoneNumber, required: true)!;
            guardian.AlternativePhoneNumber = NormalizePhoneNumber(request.AlternativePhoneNumber, required: false);
            guardian.RelationshipToChild = request.RelationshipToChild;
            guardian.Address = request.Address;
            guardian.Ward = request.Ward;
            guardian.UpdatedAt = DateTime.UtcNow;
            db.AuditLogs.Add(new AuditLog { Action = "Guardian updated", EntityType = "Guardian", EntityId = guardian.Id });
            db.ServerChangeLogs.Add(new ServerChangeLog
            {
                EntityType = "Guardian",
                EntityId = guardian.Id,
                OperationType = "Update",
                PayloadJson = ApplicationDbContext.ToJsonElement(guardian)
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
        return app;
    }

    private static string? NormalizePhoneNumber(string? phoneNumber, bool required)
    {
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            if (required)
            {
                throw new ApiException("VALIDATION_ERROR", "Phone number is required.", HttpStatusCode.BadRequest);
            }

            return null;
        }

        if (!PhoneNumberFormatter.TryNormalizeToNigerianE164(phoneNumber, out var normalized))
        {
            throw new ApiException(
                "VALIDATION_ERROR",
                $"'{phoneNumber}' is not a valid Nigerian phone number. Use a local (0801...) or international (+234801...) format.",
                HttpStatusCode.BadRequest);
        }

        return normalized;
    }
}
