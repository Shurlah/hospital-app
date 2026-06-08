using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Devices;

public static class DevicesModule
{
    public static IEndpointRouteBuilder MapDevicesModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices").WithTags("Devices").RequireAuthorization();
        group.MapPost("/register", async (RegisterDeviceRequest request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var existing = await db.Devices.SingleOrDefaultAsync(x => x.DeviceIdentifier == request.DeviceIdentifier, ct);
            if (existing is not null)
            {
                existing.LastSeenAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                return Results.Ok(existing);
            }

            var device = new Device
            {
                DeviceIdentifier = request.DeviceIdentifier,
                UserId = request.UserId,
                FacilityId = request.FacilityId,
                DeviceName = request.DeviceName,
                Platform = request.Platform,
                IsApproved = false,
                LastSeenAt = DateTime.UtcNow
            };
            db.Devices.Add(device);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/devices/{device.Id}", device);
        });
        group.MapPost("/{id:guid}/approve", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
        {
            var device = await db.Devices.FindAsync([id], ct);
            if (device is null) return Results.NotFound();
            device.IsApproved = true;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthPolicies.SystemAdminOnly);
        return app;
    }
}

public sealed record RegisterDeviceRequest(string DeviceIdentifier, Guid UserId, Guid FacilityId, string? DeviceName, string? Platform);
