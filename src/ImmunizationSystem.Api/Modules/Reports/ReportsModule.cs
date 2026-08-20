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
            var rows = await BuildImmunizationRecordsDetailQuery(db, facilityId, null, from, to).ToListAsync(ct);
            var csv = BuildCsv(
                ["facilityName", "childFirstName", "childMiddleName", "childLastName", "childDateOfBirth", "childSex", "vaccineName", "doseName", "dateAdministered", "guardianFullName", "guardianPhoneNumber"],
                rows.Select(row => new object?[]
                {
                    row.FacilityName,
                    row.ChildFirstName,
                    row.ChildMiddleName,
                    row.ChildLastName,
                    row.ChildDateOfBirth,
                    row.ChildSex,
                    row.VaccineName,
                    row.DoseName,
                    row.DateAdministered,
                    row.GuardianFullName,
                    row.GuardianPhoneNumber
                }));

            return CsvFile(csv, BuildFileName("immunization-coverage"));
        })
            .WithSummary("Export immunization coverage report as CSV")
            .WithDescription("Downloads one row per vaccine dose administered (hospital, child, vaccine, dose, date, guardian) using the same filters as the JSON coverage endpoint.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        group.MapGet("/missed-appointments", async (Guid? facilityId, ApplicationDbContext db, CancellationToken ct) =>
        {
            return Results.Ok(await BuildMissedAppointmentsReportQuery(db, facilityId)
                .Take(200)
                .ToListAsync(ct));
        })
            .WithSummary("Get missed appointments report")
            .WithDescription("Returns the most recent missed appointments (with child, guardian, vaccine, and facility names), optionally filtered to one facility.")
            .Produces<List<MissedAppointmentDetailRow>>(StatusCodes.Status200OK);

        group.MapGet("/missed-appointments/export", async (Guid? facilityId, ApplicationDbContext db, CancellationToken ct) =>
        {
            var appointments = await BuildMissedAppointmentsReportQuery(db, facilityId)
                .Take(200)
                .ToListAsync(ct);

            var csv = BuildCsv(
                ["appointmentId", "childFirstName", "childLastName", "guardianFullName", "guardianPhoneNumber", "vaccineName", "doseName", "facilityName", "appointmentDate", "status", "missedAt", "createdAt"],
                appointments.Select(appointment => new object?[]
                {
                    appointment.AppointmentId,
                    appointment.ChildFirstName,
                    appointment.ChildLastName,
                    appointment.GuardianFullName,
                    appointment.GuardianPhoneNumber,
                    appointment.VaccineName,
                    appointment.DoseName,
                    appointment.FacilityName,
                    appointment.AppointmentDate,
                    appointment.Status,
                    appointment.MissedAt,
                    appointment.CreatedAt
                }));

            return CsvFile(csv, BuildFileName("missed-appointments"));
        })
            .WithSummary("Export missed appointments report as CSV")
            .WithDescription("Downloads the missed appointments report (with child, guardian, vaccine, and facility names) as a CSV file.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv");

        group.MapGet("/sms-delivery", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await BuildSmsDeliveryReportAsync(db, ct)))
            .WithSummary("Get SMS delivery report")
            .WithDescription("Returns aggregate SMS delivery counts grouped by delivery state.")
            .Produces<List<StatusCountReportRow>>(StatusCodes.Status200OK);

        group.MapGet("/sms-delivery/export", async (ApplicationDbContext db, CancellationToken ct) =>
        {
            var notifications =
                await (from notification in db.SmsNotifications
                       join child in db.Children on notification.ChildId equals child.Id into childJoin
                       from child in childJoin.DefaultIfEmpty()
                       orderby notification.CreatedAt descending
                       select new SmsNotificationDetailRow(
                           notification.Id,
                           notification.PhoneNumber,
                           child != null ? child.FirstName + " " + child.LastName : null,
                           notification.NotificationType,
                           notification.Status,
                           notification.ScheduledAt,
                           notification.SentAt,
                           notification.DeliveredAt,
                           notification.FailedAt,
                           notification.FailureReason,
                           notification.CreatedAt))
                    .Take(500)
                    .ToListAsync(ct);

            var csv = BuildCsv(
                ["phoneNumber", "childName", "notificationType", "status", "wasSuccessful", "scheduledAt", "sentAt", "deliveredAt", "failedAt", "failureReason", "createdAt"],
                notifications.Select(row => new object?[]
                {
                    row.PhoneNumber,
                    row.ChildName,
                    row.NotificationType,
                    row.Status,
                    row.Status is SmsStatuses.Sent or SmsStatuses.Delivered,
                    row.ScheduledAt,
                    row.SentAt,
                    row.DeliveredAt,
                    row.FailedAt,
                    row.FailureReason,
                    row.CreatedAt
                }));

            return CsvFile(csv, BuildFileName("sms-delivery"));
        })
            .WithSummary("Export SMS delivery report as CSV")
            .WithDescription("Downloads the most recent SMS notifications (phone number, child, and whether delivery succeeded) as a CSV file.")
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

        group.MapGet("/immunization-records", async (
            Guid? facilityId,
            Guid? childId,
            DateOnly? from,
            DateOnly? to,
            int page,
            int pageSize,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            page = page < 1 ? 1 : page;
            pageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 200);

            var query = BuildImmunizationRecordsDetailQuery(db, facilityId, childId, from, to);
            var total = await query.CountAsync(ct);
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return Results.Ok(new { items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
        })
            .WithSummary("Get detailed immunization records")
            .WithDescription("Returns per-dose immunization records with child, guardian, vaccine, dose, date administered, and facility details. Supports filtering by facility, child, and date range on DateAdministered.")
            .Produces(StatusCodes.Status200OK);

        group.MapGet("/immunization-records/export", async (
            Guid? facilityId,
            Guid? childId,
            DateOnly? from,
            DateOnly? to,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            var rows = await BuildImmunizationRecordsDetailQuery(db, facilityId, childId, from, to)
                .ToListAsync(ct);

            var csv = BuildCsv(
                [
                    "recordId",
                    "childId",
                    "childFirstName",
                    "childMiddleName",
                    "childLastName",
                    "childDateOfBirth",
                    "childSex",
                    "guardianFullName",
                    "guardianPhoneNumber",
                    "vaccineId",
                    "vaccineName",
                    "doseName",
                    "dateAdministered",
                    "facilityId",
                    "facilityName",
                    "administeredByUserId",
                    "administeredByUserName",
                    "notes",
                    "isCorrection",
                    "createdAt"
                ],
                rows.Select(row => new object?[]
                {
                    row.RecordId,
                    row.ChildId,
                    row.ChildFirstName,
                    row.ChildMiddleName,
                    row.ChildLastName,
                    row.ChildDateOfBirth,
                    row.ChildSex,
                    row.GuardianFullName,
                    row.GuardianPhoneNumber,
                    row.VaccineId,
                    row.VaccineName,
                    row.DoseName,
                    row.DateAdministered,
                    row.FacilityId,
                    row.FacilityName,
                    row.AdministeredByUserId,
                    row.AdministeredByUserName,
                    row.Notes,
                    row.IsCorrection,
                    row.CreatedAt
                }));

            return CsvFile(csv, BuildFileName("immunization-records"));
        })
            .WithSummary("Export detailed immunization records as CSV")
            .WithDescription("Downloads per-dose immunization records (child, guardian, vaccine, dose, date, facility) as a CSV file, using the same filters as the JSON endpoint.")
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
        var children = db.Children.Where(x => x.DeletedAt == null).AsQueryable();
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

    private static IQueryable<ImmunizationRecordDetailRow> BuildImmunizationRecordsDetailQuery(
        ApplicationDbContext db,
        Guid? facilityId,
        Guid? childId,
        DateOnly? from,
        DateOnly? to)
    {
        var records = db.ImmunizationRecords.AsQueryable();

        if (facilityId.HasValue)
        {
            records = records.Where(x => x.FacilityId == facilityId);
        }

        if (childId.HasValue)
        {
            records = records.Where(x => x.ChildId == childId);
        }

        if (from.HasValue)
        {
            records = records.Where(x => x.DateAdministered >= from);
        }

        if (to.HasValue)
        {
            records = records.Where(x => x.DateAdministered <= to);
        }

        return
            from record in records
            orderby record.DateAdministered descending
            join child in db.Children on record.ChildId equals child.Id
            where child.DeletedAt == null
            join guardian in db.Guardians on child.GuardianId equals guardian.Id into guardianJoin
            from guardian in guardianJoin.DefaultIfEmpty()
            join vaccine in db.Vaccines on record.VaccineId equals vaccine.Id into vaccineJoin
            from vaccine in vaccineJoin.DefaultIfEmpty()
            join facility in db.Facilities on record.FacilityId equals facility.Id into facilityJoin
            from facility in facilityJoin.DefaultIfEmpty()
            join administeredBy in db.Users on record.AdministeredByUserId equals administeredBy.Id into userJoin
            from administeredBy in userJoin.DefaultIfEmpty()
            select new ImmunizationRecordDetailRow(
                record.Id,
                child.Id,
                child.FirstName,
                child.MiddleName,
                child.LastName,
                child.DateOfBirth,
                child.Sex,
                guardian != null ? guardian.FullName : null,
                guardian != null ? guardian.PhoneNumber : null,
                record.VaccineId,
                vaccine != null ? vaccine.Name : null,
                record.DoseName,
                record.DateAdministered,
                record.FacilityId,
                facility != null ? facility.Name : null,
                record.AdministeredByUserId,
                administeredBy != null ? administeredBy.FullName : null,
                record.Notes,
                record.IsCorrection,
                record.CreatedAt);
    }

    private static IQueryable<MissedAppointmentDetailRow> BuildMissedAppointmentsReportQuery(ApplicationDbContext db, Guid? facilityId)
    {
        var appointments = db.Appointments.Where(x => x.Status == AppointmentStatuses.Missed).AsQueryable();

        if (facilityId.HasValue)
        {
            appointments = appointments.Where(x => x.FacilityId == facilityId);
        }

        return
            from appointment in appointments
            orderby appointment.MissedAt descending
            join child in db.Children on appointment.ChildId equals child.Id into childJoin
            from child in childJoin.DefaultIfEmpty()
            join guardian in db.Guardians on child.GuardianId equals guardian.Id into guardianJoin
            from guardian in guardianJoin.DefaultIfEmpty()
            join vaccine in db.Vaccines on appointment.VaccineId equals vaccine.Id into vaccineJoin
            from vaccine in vaccineJoin.DefaultIfEmpty()
            join facility in db.Facilities on appointment.FacilityId equals facility.Id into facilityJoin
            from facility in facilityJoin.DefaultIfEmpty()
            select new MissedAppointmentDetailRow(
                appointment.Id,
                appointment.ChildId,
                child != null ? child.FirstName : null,
                child != null ? child.LastName : null,
                guardian != null ? guardian.FullName : null,
                guardian != null ? guardian.PhoneNumber : null,
                appointment.VaccineId,
                vaccine != null ? vaccine.Name : null,
                appointment.DoseName,
                appointment.FacilityId,
                facility != null ? facility.Name : null,
                appointment.AppointmentDate,
                appointment.Status,
                appointment.MissedAt,
                appointment.CreatedAt);
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
            db.Children.Count(c => c.FacilityId == f.Id && c.DeletedAt == null),
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

    private sealed record MissedAppointmentDetailRow(
        Guid AppointmentId,
        Guid ChildId,
        string? ChildFirstName,
        string? ChildLastName,
        string? GuardianFullName,
        string? GuardianPhoneNumber,
        Guid VaccineId,
        string? VaccineName,
        string DoseName,
        Guid FacilityId,
        string? FacilityName,
        DateOnly AppointmentDate,
        string Status,
        DateTime? MissedAt,
        DateTime CreatedAt);

    private sealed record SmsNotificationDetailRow(
        Guid Id,
        string PhoneNumber,
        string? ChildName,
        string NotificationType,
        string Status,
        DateTime ScheduledAt,
        DateTime? SentAt,
        DateTime? DeliveredAt,
        DateTime? FailedAt,
        string? FailureReason,
        DateTime CreatedAt);

    private sealed record ImmunizationRecordDetailRow(
        Guid RecordId,
        Guid ChildId,
        string ChildFirstName,
        string? ChildMiddleName,
        string ChildLastName,
        DateOnly ChildDateOfBirth,
        string ChildSex,
        string? GuardianFullName,
        string? GuardianPhoneNumber,
        Guid VaccineId,
        string? VaccineName,
        string DoseName,
        DateOnly DateAdministered,
        Guid FacilityId,
        string? FacilityName,
        Guid AdministeredByUserId,
        string? AdministeredByUserName,
        string? Notes,
        bool IsCorrection,
        DateTime CreatedAt);
}
