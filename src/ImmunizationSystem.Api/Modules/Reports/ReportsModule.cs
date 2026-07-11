using System.Text;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Reports;

public static class ReportsModule
{
    public static IEndpointRouteBuilder MapReportsModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization(AuthPolicies.CanViewReports);

        group.MapGet("/immunization-coverage", async (Guid? facilityId, DateOnly? from, DateOnly? to, ApplicationDbContext db, CancellationToken ct) =>
        {
            return Results.Ok(await BuildImmunizationCoverageReportAsync(db, facilityId, from, to, ct));
        })
            .WithSummary("Get immunization coverage report")
            .WithDescription("Returns aggregate immunization coverage counts for the selected facility and optional date range.")
            .Produces<ImmunizationCoverageReport>(StatusCodes.Status200OK);

        group.MapGet("/immunization-coverage/export", async (Guid? facilityId, DateOnly? from, DateOnly? to, ApplicationDbContext db, CancellationToken ct) =>
        {
            var report = await BuildImmunizationCoverageReportAsync(db, facilityId, from, to, ct);
            var csv = BuildCsv(
                ["facilityId", "from", "to", "registeredChildren", "completedImmunizations", "missedAppointments"],
                [new object?[] { facilityId, from, to, report.RegisteredChildren, report.CompletedImmunizations, report.MissedAppointments }]);

            return CsvFile(csv, BuildFileName("immunization-coverage"));
        })
            .WithSummary("Export immunization coverage report as CSV")
            .WithDescription("Downloads the immunization coverage report as a CSV file using the same filters as the JSON endpoint.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        group.MapGet("/missed-appointments", async (Guid? facilityId, ApplicationDbContext db, CancellationToken ct) =>
        {
            return Results.Ok(await BuildMissedAppointmentsReportQuery(db, facilityId)
                .OrderByDescending(x => x.MissedAt)
                .Take(200)
                .ToListAsync(ct));
        })
            .WithSummary("Get missed appointments report")
            .WithDescription("Returns the most recent missed appointments, optionally filtered to one facility.")
            .Produces<List<Appointment>>(StatusCodes.Status200OK);

        group.MapGet("/missed-appointments/export", async (Guid? facilityId, ApplicationDbContext db, CancellationToken ct) =>
        {
            var appointments = await BuildMissedAppointmentsReportQuery(db, facilityId)
                .OrderByDescending(x => x.MissedAt)
                .Take(200)
                .ToListAsync(ct);

            var csv = BuildCsv(
                ["appointmentId", "childId", "vaccineId", "doseName", "facilityId", "appointmentDate", "status", "missedAt", "createdAt"],
                appointments.Select(appointment => new object?[]
                {
                    appointment.Id,
                    appointment.ChildId,
                    appointment.VaccineId,
                    appointment.DoseName,
                    appointment.FacilityId,
                    appointment.AppointmentDate,
                    appointment.Status,
                    appointment.MissedAt,
                    appointment.CreatedAt
                }));

            return CsvFile(csv, BuildFileName("missed-appointments"));
        })
            .WithSummary("Export missed appointments report as CSV")
            .WithDescription("Downloads the missed appointments report as a CSV file.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        group.MapGet("/sms-delivery", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await BuildSmsDeliveryReportAsync(db, ct)))
            .WithSummary("Get SMS delivery report")
            .WithDescription("Returns aggregate SMS delivery counts grouped by delivery state.")
            .Produces<List<StatusCountReportRow>>(StatusCodes.Status200OK);

        group.MapGet("/sms-delivery/export", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var report = await BuildSmsDeliveryReportAsync(db, ct);
            var csv = BuildCsv(
                ["status", "count"],
                report.Select(row => new object?[] { row.Status, row.Count }));

            return CsvFile(csv, BuildFileName("sms-delivery"));
        })
            .WithSummary("Export SMS delivery report as CSV")
            .WithDescription("Downloads aggregate SMS delivery counts as a CSV file.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        group.MapGet("/sync-reliability", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await BuildSyncReliabilityReportAsync(db, ct)))
            .WithSummary("Get sync reliability report")
            .WithDescription("Returns aggregate synchronization processing counts grouped by processing status.")
            .Produces<List<StatusCountReportRow>>(StatusCodes.Status200OK);

        group.MapGet("/sync-reliability/export", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var report = await BuildSyncReliabilityReportAsync(db, ct);
            var csv = BuildCsv(
                ["status", "count"],
                report.Select(row => new object?[] { row.Status, row.Count }));

            return CsvFile(csv, BuildFileName("sync-reliability"));
        })
            .WithSummary("Export sync reliability report as CSV")
            .WithDescription("Downloads aggregate synchronization processing counts as a CSV file.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        group.MapGet("/facility-performance", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await BuildFacilityPerformanceReportQuery(db).ToListAsync(ct)))
            .WithSummary("Get facility performance report")
            .WithDescription("Returns facility-level child registration, immunization, and missed appointment counts.")
            .Produces<List<FacilityPerformanceReportRow>>(StatusCodes.Status200OK);

        group.MapGet("/facility-performance/export", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var report = await BuildFacilityPerformanceReportQuery(db).ToListAsync(ct);
            var csv = BuildCsv(
                ["facilityId", "name", "children", "immunizations", "missedAppointments"],
                report.Select(row => new object?[] { row.FacilityId, row.Name, row.Children, row.Immunizations, row.MissedAppointments }));

            return CsvFile(csv, BuildFileName("facility-performance"));
        })
            .WithSummary("Export facility performance report as CSV")
            .WithDescription("Downloads facility-level performance metrics as a CSV file.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        return app;
    }

    private static async Task<ImmunizationCoverageReport> BuildImmunizationCoverageReportAsync(
        ApplicationDbContext db,
        Guid? facilityId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        var children = db.Children.AsQueryable();
        var records = db.ImmunizationRecords.AsQueryable();

        if (facilityId.HasValue)
        {
            children = children.Where(x => x.FacilityId == facilityId);
            records = records.Where(x => x.FacilityId == facilityId);
        }

        if (from.HasValue)
        {
            records = records.Where(x => x.DateAdministered >= from);
        }

        if (to.HasValue)
        {
            records = records.Where(x => x.DateAdministered <= to);
        }

        return new ImmunizationCoverageReport(
            await children.CountAsync(ct),
            await records.CountAsync(ct),
            await db.Appointments.CountAsync(
                x => x.Status == AppointmentStatuses.Missed && (!facilityId.HasValue || x.FacilityId == facilityId),
                ct));
    }

    private static IQueryable<Appointment> BuildMissedAppointmentsReportQuery(ApplicationDbContext db, Guid? facilityId)
    {
        var query = db.Appointments.Where(x => x.Status == AppointmentStatuses.Missed);

        if (facilityId.HasValue)
        {
            query = query.Where(x => x.FacilityId == facilityId);
        }

        return query;
    }

    private static Task<List<StatusCountReportRow>> BuildSmsDeliveryReportAsync(ApplicationDbContext db, CancellationToken ct)
        => BuildStatusCountReportAsync(
            db.SmsNotifications.Select(x => x.Status),
            [SmsStatuses.Sent, SmsStatuses.Delivered, SmsStatuses.Failed],
            ct);

    private static Task<List<StatusCountReportRow>> BuildSyncReliabilityReportAsync(ApplicationDbContext db, CancellationToken ct)
        => BuildStatusCountReportAsync(
            db.SyncInbox.Select(x => x.Status),
            ["Accepted", "Failed", "Conflict"],
            ct,
            includeTotal: true);

    private static IQueryable<FacilityPerformanceReportRow> BuildFacilityPerformanceReportQuery(ApplicationDbContext db) =>
        db.Facilities.Select(f => new FacilityPerformanceReportRow(
            f.Id,
            f.Name,
            db.Children.Count(c => c.FacilityId == f.Id),
            db.ImmunizationRecords.Count(i => i.FacilityId == f.Id),
            db.Appointments.Count(a => a.FacilityId == f.Id && a.Status == AppointmentStatuses.Missed)));

    private static async Task<List<StatusCountReportRow>> BuildStatusCountReportAsync(
        IQueryable<string> statusQuery,
        IReadOnlyList<string> statuses,
        CancellationToken ct,
        bool includeTotal = false)
    {
        var groupedCounts = await statusQuery
            .GroupBy(status => status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        var report = statuses
            .Select(status => new StatusCountReportRow(status, groupedCounts.GetValueOrDefault(status, 0)))
            .ToList();

        if (includeTotal)
        {
            report.Insert(0, new StatusCountReportRow("Total", groupedCounts.Values.Sum()));
        }

        return report;
    }

    private static IResult CsvFile(string csv, string fileName)
        => Results.File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);

    private static string BuildCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(",", row.Select(FormatCsvValue)));
        }

        return builder.ToString();
    }

    private static string FormatCsvValue(object? value) => value switch
    {
        null => string.Empty,
        DateOnly date => EscapeCsv(date.ToString("yyyy-MM-dd")),
        DateTime dateTime => EscapeCsv(dateTime.ToString("O")),
        DateTimeOffset dateTimeOffset => EscapeCsv(dateTimeOffset.ToString("O")),
        bool boolean => EscapeCsv(boolean ? "true" : "false"),
        _ => EscapeCsv(value.ToString() ?? string.Empty)
    };

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny([',', '"', '\n', '\r']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string BuildFileName(string reportName)
        => $"{reportName}-{DateTime.UtcNow:yyyyMMddHHmmss}.csv";

    private sealed record ImmunizationCoverageReport(
        int RegisteredChildren,
        int CompletedImmunizations,
        int MissedAppointments);

    private sealed record StatusCountReportRow(
        string Status,
        int Count);

    private sealed record FacilityPerformanceReportRow(
        Guid FacilityId,
        string Name,
        int Children,
        int Immunizations,
        int MissedAppointments);
}
