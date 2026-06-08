using System.Net;
using ImmunizationSystem.Api.Shared.Cqrs;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Errors;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Immunizations;

public static class ImmunizationsModule
{
    public static IEndpointRouteBuilder MapImmunizationsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/immunizations").WithTags("Immunizations").RequireAuthorization(AuthPolicies.CanRecordImmunization);
        group.MapPost("/", async (RecordImmunizationCommand command, IRequestDispatcher dispatcher, CancellationToken ct) =>
        {
            var record = await dispatcher.SendAsync(command, ct);
            return Results.Created($"/api/immunizations/{record.Id}", record);
        });
        group.MapGet("/child/{childId:guid}", async (Guid childId, ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ImmunizationRecords.Where(x => x.ChildId == childId).OrderBy(x => x.DateAdministered).ToListAsync(ct)));
        group.MapPost("/{id:guid}/corrections", async (Guid id, CorrectionRequest request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var original = await db.ImmunizationRecords.FindAsync([id], ct);
            if (original is null) return Results.NotFound();
            var correction = new ImmunizationRecord
            {
                ChildId = original.ChildId,
                VaccineId = original.VaccineId,
                DoseName = original.DoseName,
                DateAdministered = request.DateAdministered,
                FacilityId = original.FacilityId,
                AdministeredByUserId = request.CorrectedByUserId,
                Notes = request.Notes,
                IsCorrection = true,
                CorrectedRecordId = original.Id
            };
            db.ImmunizationRecords.Add(correction);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/immunizations/{correction.Id}", correction);
        });
        return app;
    }
}

public sealed record RecordImmunizationCommand(Guid? Id, Guid ChildId, Guid VaccineId, string DoseName, DateOnly DateAdministered, Guid FacilityId, Guid AdministeredByUserId, Guid? CreatedByDeviceId, string? Notes)
    : ICommand<ImmunizationRecord>;

public sealed record CorrectionRequest(DateOnly DateAdministered, Guid CorrectedByUserId, string? Notes);

public sealed class RecordImmunizationHandler(ApplicationDbContext dbContext) : ICommandHandler<RecordImmunizationCommand, ImmunizationRecord>
{
    public async Task<ImmunizationRecord> HandleAsync(RecordImmunizationCommand command, CancellationToken cancellationToken)
    {
        var duplicate = await dbContext.ImmunizationRecords.AnyAsync(x =>
            x.ChildId == command.ChildId && x.VaccineId == command.VaccineId && x.DoseName == command.DoseName && !x.IsCorrection,
            cancellationToken);
        if (duplicate) throw new ApiException("DUPLICATE_RECORD", "This vaccine dose has already been recorded.", HttpStatusCode.Conflict);

        var record = new ImmunizationRecord
        {
            Id = command.Id ?? Guid.NewGuid(),
            ChildId = command.ChildId,
            VaccineId = command.VaccineId,
            DoseName = command.DoseName,
            DateAdministered = command.DateAdministered,
            FacilityId = command.FacilityId,
            AdministeredByUserId = command.AdministeredByUserId,
            CreatedByDeviceId = command.CreatedByDeviceId,
            Notes = command.Notes
        };
        dbContext.ImmunizationRecords.Add(record);
        dbContext.AuditLogs.Add(new AuditLog { UserId = command.AdministeredByUserId, DeviceId = command.CreatedByDeviceId, Action = "Immunization recorded", EntityType = "ImmunizationRecord", EntityId = record.Id });
        dbContext.ServerChangeLogs.Add(new ServerChangeLog { EntityType = "ImmunizationRecord", EntityId = record.Id, OperationType = "Create", FacilityId = record.FacilityId, CreatedByUserId = record.AdministeredByUserId, PayloadJson = ApplicationDbContext.ToJsonElement(record) });
        await dbContext.SaveChangesAsync(cancellationToken);
        return record;
    }
}
