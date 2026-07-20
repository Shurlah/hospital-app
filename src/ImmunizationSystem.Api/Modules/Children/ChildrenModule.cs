using System.Text;
using ImmunizationSystem.Api.Modules.Appointments;
using ImmunizationSystem.Api.Shared.Cqrs;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Children;

public static class ChildrenModule
{
    public static IEndpointRouteBuilder MapChildrenModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/children").WithTags("Children").RequireAuthorization();
        group.MapPost("/", async (RegisterChildCommand command, IRequestDispatcher dispatcher, CancellationToken ct) =>
        {
            var child = await dispatcher.SendAsync(command, ct);
            return Results.Created($"/api/children/{child.Id}", child);
        }).RequireAuthorization(AuthPolicies.CanRecordImmunization);
        group.MapGet("/", async (ApplicationDbContext db, Guid? facilityId, int page = 1, int pageSize = 20, CancellationToken ct = default) =>
        {
            var query = db.Children.Include(x => x.Guardian).AsQueryable();
            if (facilityId.HasValue) query = query.Where(x => x.FacilityId == facilityId);
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
            return Results.Ok(new { items, page, pageSize, totalCount = total, totalPages = (int)Math.Ceiling(total / (double)pageSize) });
        });
        group.MapGet("/export", async (
            Guid? facilityId,
            DateOnly? from,
            DateOnly? to,
            string? startMonth,
            string? endMonth,
            int? startYear,
            int? endYear,
            ApplicationDbContext db,
            CancellationToken ct) =>
        {
            var filterResult = ResolveCreatedAtFilter(from, to, startMonth, endMonth, startYear, endYear);
            if (filterResult.Error is not null)
            {
                return Results.ValidationProblem(filterResult.Error);
            }

            var query = BuildChildrenExportQuery(db, facilityId, filterResult.Filter);
            var rows = await query
                .OrderByDescending(x => x.CreatedAt)
                .Select(x => new ChildExportRow(
                    x.Id,
                    x.FirstName,
                    x.MiddleName,
                    x.LastName,
                    x.DateOfBirth,
                    x.Sex,
                    x.FacilityId,
                    x.RegistrationSource,
                    x.CreatedByUserId,
                    x.CreatedByDeviceId,
                    x.IsPossibleDuplicate,
                    x.CreatedAt,
                    x.UpdatedAt,
                    x.GuardianId,
                    x.Guardian != null ? x.Guardian.FullName : null,
                    x.Guardian != null ? x.Guardian.PhoneNumber : null,
                    x.Guardian != null ? x.Guardian.AlternativePhoneNumber : null,
                    x.Guardian != null ? x.Guardian.RelationshipToChild : null,
                    x.Guardian != null ? x.Guardian.Address : null,
                    x.Guardian != null ? x.Guardian.Ward : null))
                .ToListAsync(ct);

            var csv = BuildCsv(
                [
                    "childId",
                    "firstName",
                    "middleName",
                    "lastName",
                    "dateOfBirth",
                    "sex",
                    "facilityId",
                    "registrationSource",
                    "createdByUserId",
                    "createdByDeviceId",
                    "isPossibleDuplicate",
                    "createdAt",
                    "updatedAt",
                    "guardianId",
                    "guardianFullName",
                    "guardianPhoneNumber",
                    "guardianAlternativePhoneNumber",
                    "guardianRelationshipToChild",
                    "guardianAddress",
                    "guardianWard"
                ],
                rows.Select(row => new object?[]
                {
                    row.ChildId,
                    row.FirstName,
                    row.MiddleName,
                    row.LastName,
                    row.DateOfBirth,
                    row.Sex,
                    row.FacilityId,
                    row.RegistrationSource,
                    row.CreatedByUserId,
                    row.CreatedByDeviceId,
                    row.IsPossibleDuplicate,
                    row.CreatedAt,
                    row.UpdatedAt,
                    row.GuardianId,
                    row.GuardianFullName,
                    row.GuardianPhoneNumber,
                    row.GuardianAlternativePhoneNumber,
                    row.GuardianRelationshipToChild,
                    row.GuardianAddress,
                    row.GuardianWard
                }));

            return Results.File(
                Encoding.UTF8.GetBytes(csv),
                "text/csv; charset=utf-8",
                $"children-export-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
        })
            .RequireAuthorization(AuthPolicies.CanViewReports)
            .WithSummary("Export children data as CSV")
            .WithDescription("Exports all child records with guardian details as CSV. Supports one filter mode at a time: `from` and `to` for a date range on `CreatedAt`, `startMonth` and `endMonth` in `yyyy-MM`, or `startYear` and `endYear` as four-digit years. If no filter is supplied, all children are exported.")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .ProducesValidationProblem();
        group.MapGet("/{id:guid}/due-vaccines", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
        {
            var child = await db.Children.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (child is null) return Results.NotFound();

            var schedules = await db.VaccineSchedules
                .Include(x => x.Vaccine)
                .Where(x => x.IsActive && x.Vaccine != null && x.Vaccine.IsActive)
                .ToListAsync(ct);
            var records = await db.ImmunizationRecords
                .Where(x => x.ChildId == id)
                .ToListAsync(ct);
            var appointments = await db.Appointments
                .Where(x => x.ChildId == id && x.Status != AppointmentStatuses.Cancelled)
                .ToListAsync(ct);

            return Results.Ok(ChildSchedulePlanner.BuildDueVaccines(
                child,
                schedules,
                records,
                appointments,
                AppointmentNotificationScheduler.GetSchedulingToday()));
        })
            .RequireAuthorization(AuthPolicies.CanViewReports)
            .WithSummary("Get due vaccines for a child")
            .WithDescription("Calculates the child's vaccine schedule from date of birth and active vaccine schedules, then marks each dose as completed, scheduled, overdue, due today, or upcoming.")
            .Produces<List<ChildDueVaccineItem>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/generate-appointments", async (Guid id, GenerateScheduleAppointmentsRequest? request, ApplicationDbContext db, CancellationToken ct) =>
        {
            var child = await db.Children.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (child is null) return Results.NotFound();

            var throughDate = request?.ThroughDate ?? AppointmentNotificationScheduler.GetSchedulingToday().AddDays(84);
            var schedules = await db.VaccineSchedules
                .Include(x => x.Vaccine)
                .Where(x => x.IsActive && x.Vaccine != null && x.Vaccine.IsActive)
                .ToListAsync(ct);
            var records = await db.ImmunizationRecords
                .Where(x => x.ChildId == id)
                .ToListAsync(ct);
            var appointments = await db.Appointments
                .Where(x => x.ChildId == id && x.Status != AppointmentStatuses.Cancelled)
                .ToListAsync(ct);

            var dueVaccines = ChildSchedulePlanner.BuildDueVaccines(
                child,
                schedules,
                records,
                appointments,
                AppointmentNotificationScheduler.GetSchedulingToday());
            var generatedAppointments = ChildSchedulePlanner.BuildAppointmentsForDueVaccines(child, dueVaccines, throughDate);

            foreach (var appointment in generatedAppointments)
            {
                db.Appointments.Add(appointment);
                db.AuditLogs.Add(new AuditLog
                {
                    UserId = request?.CreatedByUserId,
                    Action = "Appointment created from vaccine schedule",
                    EntityType = "Appointment",
                    EntityId = appointment.Id
                });
                await AppointmentNotificationScheduler.ScheduleReminderAsync(db, appointment, ct);
            }

            await db.SaveChangesAsync(ct);

            return Results.Ok(new GenerateScheduleAppointmentsResult(
                throughDate,
                generatedAppointments.Count,
                generatedAppointments.Select(x => new GeneratedAppointmentItem(x.Id, x.VaccineId, x.DoseName, x.AppointmentDate, x.AppointmentTime)).ToList()));
        })
            .RequireAuthorization(AuthPolicies.CanRecordImmunization)
            .WithSummary("Generate appointments from vaccine schedules")
            .WithDescription("Creates scheduled appointments for incomplete child vaccine doses due on or before the selected through date. Existing scheduled appointments and completed doses are skipped.")
            .Produces<GenerateScheduleAppointmentsResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}", async (Guid id, ApplicationDbContext db, CancellationToken ct) =>
            await db.Children.Include(x => x.Guardian).SingleOrDefaultAsync(x => x.Id == id, ct) is { } child ? Results.Ok(child) : Results.NotFound());
        group.MapGet("/search", async (string? q, string? phone, Guid? facilityId, ApplicationDbContext db, CancellationToken ct) =>
        {
            var query = db.Children.Include(x => x.Guardian).AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => (x.FirstName + " " + x.LastName).ToLower().Contains(q.ToLower()));
            if (!string.IsNullOrWhiteSpace(phone)) query = query.Where(x => x.Guardian!.PhoneNumber.Contains(phone));
            if (facilityId.HasValue) query = query.Where(x => x.FacilityId == facilityId);
            return Results.Ok(await query.Take(50).ToListAsync(ct));
        });
        group.MapGet("/duplicates", async (ApplicationDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Children.Include(x => x.Guardian).Where(x => x.IsPossibleDuplicate).ToListAsync(ct)));
        return app;
    }

    private static IQueryable<Child> BuildChildrenExportQuery(
        ApplicationDbContext db,
        Guid? facilityId,
        CreatedAtFilter? filter)
    {
        var query = db.Children.Include(x => x.Guardian).AsQueryable();

        if (facilityId.HasValue)
        {
            query = query.Where(x => x.FacilityId == facilityId);
        }

        if (filter is not null)
        {
            query = query.Where(x => x.CreatedAt >= filter.FromInclusiveUtc && x.CreatedAt < filter.ToExclusiveUtc);
        }

        return query;
    }

    private static CreatedAtFilterResolution ResolveCreatedAtFilter(
        DateOnly? from,
        DateOnly? to,
        string? startMonth,
        string? endMonth,
        int? startYear,
        int? endYear)
    {
        var hasDateRange = from.HasValue || to.HasValue;
        var hasMonthRange = !string.IsNullOrWhiteSpace(startMonth) || !string.IsNullOrWhiteSpace(endMonth);
        var hasYearRange = startYear.HasValue || endYear.HasValue;
        var activeModes = new[] { hasDateRange, hasMonthRange, hasYearRange }.Count(x => x);

        if (activeModes > 1)
        {
            return CreatedAtFilterResolution.Invalid("Choose only one filter mode: date range, month range, or year range.");
        }

        if (hasDateRange)
        {
            if (!from.HasValue || !to.HasValue)
            {
                return CreatedAtFilterResolution.Invalid("Both from and to must be provided for a date range export.");
            }

            if (from > to)
            {
                return CreatedAtFilterResolution.Invalid("The from date must be earlier than or equal to the to date.");
            }

            var fromUtc = from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toUtc = to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            return CreatedAtFilterResolution.Valid(new CreatedAtFilter(fromUtc, toUtc));
        }

        if (hasMonthRange)
        {
            if (string.IsNullOrWhiteSpace(startMonth) || string.IsNullOrWhiteSpace(endMonth))
            {
                return CreatedAtFilterResolution.Invalid("Both startMonth and endMonth must be provided in yyyy-MM format.");
            }

            if (!TryParseMonth(startMonth, out var fromMonth) || !TryParseMonth(endMonth, out var toMonth))
            {
                return CreatedAtFilterResolution.Invalid("startMonth and endMonth must use yyyy-MM format.");
            }

            if (fromMonth > toMonth)
            {
                return CreatedAtFilterResolution.Invalid("startMonth must be earlier than or equal to endMonth.");
            }

            var fromUtc = fromMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            var toUtc = toMonth.AddMonths(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            return CreatedAtFilterResolution.Valid(new CreatedAtFilter(fromUtc, toUtc));
        }

        if (hasYearRange)
        {
            if (!startYear.HasValue || !endYear.HasValue)
            {
                return CreatedAtFilterResolution.Invalid("Both startYear and endYear must be provided.");
            }

            if (startYear < 1 || endYear < 1)
            {
                return CreatedAtFilterResolution.Invalid("startYear and endYear must be positive years.");
            }

            if (startYear > endYear)
            {
                return CreatedAtFilterResolution.Invalid("startYear must be earlier than or equal to endYear.");
            }

            var fromYear = new DateOnly(startYear.Value, 1, 1);
            var toYear = new DateOnly(endYear.Value + 1, 1, 1);
            return CreatedAtFilterResolution.Valid(new CreatedAtFilter(
                fromYear.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                toYear.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)));
        }

        return CreatedAtFilterResolution.Valid(null);
    }

    private static bool TryParseMonth(string value, out DateOnly month)
        => DateOnly.TryParseExact($"{value}-01", "yyyy-MM-dd", out month);

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
}

internal sealed record ChildExportRow(
    Guid ChildId,
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly DateOfBirth,
    string Sex,
    Guid FacilityId,
    string RegistrationSource,
    Guid CreatedByUserId,
    Guid? CreatedByDeviceId,
    bool IsPossibleDuplicate,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    Guid GuardianId,
    string? GuardianFullName,
    string? GuardianPhoneNumber,
    string? GuardianAlternativePhoneNumber,
    string? GuardianRelationshipToChild,
    string? GuardianAddress,
    string? GuardianWard);

internal sealed record CreatedAtFilter(DateTime FromInclusiveUtc, DateTime ToExclusiveUtc);

internal sealed record CreatedAtFilterResolution(CreatedAtFilter? Filter, Dictionary<string, string[]>? Error)
{
    public static CreatedAtFilterResolution Valid(CreatedAtFilter? filter) => new(filter, null);

    public static CreatedAtFilterResolution Invalid(string message)
        => new(null, new Dictionary<string, string[]>
        {
            ["filters"] = [message]
        });
}

public sealed record GenerateScheduleAppointmentsRequest(DateOnly? ThroughDate, Guid? CreatedByUserId);

public sealed record GenerateScheduleAppointmentsResult(
    DateOnly ThroughDate,
    int CreatedCount,
    List<GeneratedAppointmentItem> Appointments);

public sealed record GeneratedAppointmentItem(
    Guid AppointmentId,
    Guid VaccineId,
    string DoseName,
    DateOnly AppointmentDate,
    TimeOnly AppointmentTime);

public sealed record RegisterChildCommand(
    Guid? Id,
    string FirstName,
    string? MiddleName,
    string LastName,
    DateOnly DateOfBirth,
    string Sex,
    Guid GuardianId,
    Guid FacilityId,
    Guid CreatedByUserId,
    Guid? CreatedByDeviceId) : ICommand<Child>;

public sealed class RegisterChildHandler(ApplicationDbContext dbContext) : ICommandHandler<RegisterChildCommand, Child>
{
    public async Task<Child> HandleAsync(RegisterChildCommand command, CancellationToken cancellationToken)
    {
        var guardian = await dbContext.Guardians.FindAsync([command.GuardianId], cancellationToken);
        var possibleDuplicate = guardian is not null && await dbContext.Children.Include(x => x.Guardian).AnyAsync(x =>
            x.FirstName == command.FirstName &&
            x.LastName == command.LastName &&
            x.DateOfBirth == command.DateOfBirth &&
            x.Sex == command.Sex &&
            x.FacilityId == command.FacilityId &&
            x.Guardian!.PhoneNumber == guardian.PhoneNumber,
            cancellationToken);

        var child = new Child
        {
            Id = command.Id ?? Guid.NewGuid(),
            FirstName = command.FirstName,
            MiddleName = command.MiddleName,
            LastName = command.LastName,
            DateOfBirth = command.DateOfBirth,
            Sex = command.Sex,
            GuardianId = command.GuardianId,
            FacilityId = command.FacilityId,
            CreatedByUserId = command.CreatedByUserId,
            CreatedByDeviceId = command.CreatedByDeviceId,
            IsPossibleDuplicate = possibleDuplicate
        };
        dbContext.Children.Add(child);
        dbContext.AuditLogs.Add(new AuditLog { UserId = command.CreatedByUserId, DeviceId = command.CreatedByDeviceId, Action = "Child registered", EntityType = "Child", EntityId = child.Id });
        dbContext.ServerChangeLogs.Add(new ServerChangeLog
        {
            EntityType = "Child",
            EntityId = child.Id,
            OperationType = "Create",
            FacilityId = child.FacilityId,
            CreatedByUserId = child.CreatedByUserId,
            PayloadJson = ApplicationDbContext.ToJsonElement(child)
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return child;
    }
}
