using System.Text.Json;
using ImmunizationSystem.Api.Modules.Children;
using ImmunizationSystem.Api.Modules.Immunizations;
using ImmunizationSystem.Api.Shared.Cqrs;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Sync;

public static class SyncModule
{
    public static IEndpointRouteBuilder MapSyncModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/sync").WithTags("Sync").RequireAuthorization(AuthPolicies.CanSyncDevice);
        group.MapPost("/upload", async (UploadSyncBatchRequest request, IRequestDispatcher dispatcher, ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await dispatcher.SendAsync(new UploadSyncBatchCommand(request), ct)));
        group.MapGet("/download", async (long sinceVersion, ApplicationDbContext db, CancellationToken ct) =>
        {
            var logs = await db.ServerChangeLogs
                .Where(x => x.ChangeVersion > sinceVersion)
                .OrderBy(x => x.ChangeVersion)
                .Take(500)
                .ToListAsync(ct);

            var changes = logs
                .Select(x => new ServerChangeDto(
                    x.ChangeVersion,
                    x.EntityType,
                    x.EntityId,
                    x.OperationType,
                    x.PayloadJson.Clone(),
                    x.CreatedAt))
                .ToList();

            var serverVersion = await db.ServerChangeLogs.MaxAsync(x => (long?)x.ChangeVersion, ct) ?? sinceVersion;
            return Results.Ok(new DownloadSyncResponse(serverVersion, changes));
        });
        group.MapGet("/status", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(new
            {
                processed = await db.SyncInbox.CountAsync(ct),
                conflicts = await db.SyncInbox.CountAsync(x => x.Status == "Conflict", ct),
                failed = await db.SyncInbox.CountAsync(x => x.Status == "Failed", ct),
                latestServerVersion = await db.ServerChangeLogs.MaxAsync(x => (long?)x.ChangeVersion, ct) ?? 0
            }));
        return app;
    }
}

public sealed record UploadSyncBatchRequest(Guid DeviceId, Guid FacilityId, Guid HealthWorkerId, IReadOnlyList<SyncChangeDto> Changes);

public sealed record SyncChangeDto(Guid ClientChangeId, string EntityType, Guid EntityId, string OperationType, JsonElement Payload, DateTime ClientTimestamp);

public sealed record UploadSyncBatchResponse(Guid SyncBatchId, long ServerVersion, IReadOnlyList<SyncItemResult> Results);

public sealed record SyncItemResult(Guid ClientChangeId, Guid EntityId, string Status, string Message);

public sealed record DownloadSyncResponse(long ServerVersion, IReadOnlyList<ServerChangeDto> Changes);

public sealed record ServerChangeDto(long ChangeVersion, string EntityType, Guid EntityId, string OperationType, JsonElement Payload, DateTime ServerTimestamp);

public sealed record UploadSyncBatchCommand(UploadSyncBatchRequest Request) : ICommand<UploadSyncBatchResponse>;

public sealed class UploadSyncBatchHandler(ApplicationDbContext dbContext, IRequestDispatcher dispatcher)
    : ICommandHandler<UploadSyncBatchCommand, UploadSyncBatchResponse>
{
    public async Task<UploadSyncBatchResponse> HandleAsync(UploadSyncBatchCommand command, CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid();
        var results = new List<SyncItemResult>();

        foreach (var change in command.Request.Changes)
        {
            var existing = await dbContext.SyncInbox.SingleOrDefaultAsync(
                x => x.DeviceId == command.Request.DeviceId && x.ClientChangeId == change.ClientChangeId,
                cancellationToken);
            if (existing is not null)
            {
                results.Add(new SyncItemResult(change.ClientChangeId, change.EntityId, existing.Status, existing.ResultMessage ?? "Already processed."));
                continue;
            }

            try
            {
                await ProcessChangeAsync(command.Request, change, cancellationToken);
                dbContext.SyncInbox.Add(new SyncInbox
                {
                    ClientChangeId = change.ClientChangeId,
                    DeviceId = command.Request.DeviceId,
                    EntityType = change.EntityType,
                    EntityId = change.EntityId,
                    OperationType = change.OperationType,
                    Status = "Accepted",
                    ResultMessage = "Accepted",
                    ProcessedAt = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                results.Add(new SyncItemResult(change.ClientChangeId, change.EntityId, "Accepted", "Synced successfully."));
            }
            catch (Exception ex)
            {
                dbContext.SyncInbox.Add(new SyncInbox
                {
                    ClientChangeId = change.ClientChangeId,
                    DeviceId = command.Request.DeviceId,
                    EntityType = change.EntityType,
                    EntityId = change.EntityId,
                    OperationType = change.OperationType,
                    Status = "Failed",
                    ResultMessage = ex.Message,
                    ProcessedAt = DateTime.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);
                results.Add(new SyncItemResult(change.ClientChangeId, change.EntityId, "Failed", ex.Message));
            }
        }

        var serverVersion = await dbContext.ServerChangeLogs.MaxAsync(x => (long?)x.ChangeVersion, cancellationToken) ?? 0;
        dbContext.AuditLogs.Add(new AuditLog { UserId = command.Request.HealthWorkerId, DeviceId = command.Request.DeviceId, Action = "Sync batch uploaded", EntityType = "SyncBatch", EntityId = batchId });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new UploadSyncBatchResponse(batchId, serverVersion, results);
    }

    private async Task ProcessChangeAsync(UploadSyncBatchRequest request, SyncChangeDto change, CancellationToken cancellationToken)
    {
        if (change.EntityType == "Guardian" && change.OperationType == "Create")
        {
            var guardian = change.Payload.Deserialize<Guardian>(JsonOptions.Default)!;
            guardian.Id = change.EntityId;
            dbContext.Guardians.Add(guardian);
            return;
        }

        if (change.EntityType == "Child" && change.OperationType == "Create")
        {
            var child = change.Payload.Deserialize<RegisterChildCommand>(JsonOptions.Default)! with
            {
                Id = change.EntityId,
                FacilityId = request.FacilityId,
                CreatedByUserId = request.HealthWorkerId,
                CreatedByDeviceId = request.DeviceId
            };
            await dispatcher.SendAsync(child, cancellationToken);
            return;
        }

        if (change.EntityType == "ImmunizationRecord" && change.OperationType == "Create")
        {
            var record = change.Payload.Deserialize<RecordImmunizationCommand>(JsonOptions.Default)! with
            {
                Id = change.EntityId,
                FacilityId = request.FacilityId,
                AdministeredByUserId = request.HealthWorkerId,
                CreatedByDeviceId = request.DeviceId
            };
            await dispatcher.SendAsync(record, cancellationToken);
            return;
        }

        if (change.EntityType == "Appointment" && change.OperationType == "Create")
        {
            var appointment = change.Payload.Deserialize<Appointment>(JsonOptions.Default)!;
            appointment.Id = change.EntityId;
            appointment.FacilityId = request.FacilityId;
            dbContext.Appointments.Add(appointment);
            return;
        }

        throw new InvalidOperationException($"Unsupported sync change {change.EntityType}/{change.OperationType}.");
    }
}
