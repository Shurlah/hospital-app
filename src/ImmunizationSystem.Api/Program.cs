using System.Text;
using ImmunizationSystem.Api.Modules.Appointments;
using ImmunizationSystem.Api.Modules.AuditLogs;
using ImmunizationSystem.Api.Modules.Auth;
using ImmunizationSystem.Api.Modules.Children;
using ImmunizationSystem.Api.Modules.Devices;
using ImmunizationSystem.Api.Modules.Facilities;
using ImmunizationSystem.Api.Modules.Guardians;
using ImmunizationSystem.Api.Modules.Immunizations;
using ImmunizationSystem.Api.Modules.Notifications;
using ImmunizationSystem.Api.Modules.Reports;
using ImmunizationSystem.Api.Modules.Sync;
using ImmunizationSystem.Api.Modules.Users;
using ImmunizationSystem.Api.Modules.Vaccines;
using ImmunizationSystem.Api.Shared.Cqrs;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Errors;
using ImmunizationSystem.Api.Shared.Security;
using ImmunizationSystem.Api.Shared.Sms;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
const string CorsPolicyName = "ConfiguredFrontendOrigins";

builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Immunization System API",
        Version = "v1",
        Description = "REST API for facility immunization workflows, offline sync, SMS reminders, reports, and audit trails."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = JwtBearerDefaults.AuthenticationScheme,
        BearerFormat = "JWT",
        Description = "Enter a JWT access token from /api/auth/login."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
    });
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddProblemDetails();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        var allowedOrigins = GetAllowedCorsOrigins(builder.Configuration, builder.Environment);

        if (allowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? builder.Configuration["ConnectionStrings__DefaultConnection"]
        ?? "Host=localhost;Port=5432;Database=immunization_system;Username=postgres;Password=postgres";

    options.UseNpgsql(connectionString);
});

builder.Services.AddScoped<IRequestDispatcher, RequestDispatcher>();
builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<ISmsSender, LoggingSmsSender>();
builder.Services.AddHostedService<SmsReminderWorker>();
builder.Services.AddFeatureHandlers();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(AuthPolicies.SystemAdminOnly, p => p.RequireRole(RoleNames.SystemAdministrator));
    options.AddPolicy(AuthPolicies.CanManageUsers, p => p.RequireRole(RoleNames.SystemAdministrator));
    options.AddPolicy(AuthPolicies.CanManageFacilities, p => p.RequireRole(RoleNames.SystemAdministrator));
    options.AddPolicy(AuthPolicies.CanViewReports, p => p.RequireRole(RoleNames.SystemAdministrator, RoleNames.LgaHealthOfficial, RoleNames.FacilitySupervisor, RoleNames.Auditor));
    options.AddPolicy(AuthPolicies.CanRecordImmunization, p => p.RequireRole(RoleNames.HealthWorker, RoleNames.FacilitySupervisor, RoleNames.SystemAdministrator));
    options.AddPolicy(AuthPolicies.CanSyncDevice, p => p.RequireRole(RoleNames.HealthWorker, RoleNames.FacilitySupervisor, RoleNames.SystemAdministrator));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await DatabaseSeeder.SeedSuperAdminAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Immunization System API v1");
        options.RoutePrefix = "swagger";
        options.DocumentTitle = "Immunization System API";
    });
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseHttpsRedirection();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy", service = "ImmunizationSystem.Api" }));

app.MapAuthModule();
app.MapUsersModule();
app.MapFacilitiesModule();
app.MapDevicesModule();
app.MapGuardiansModule();
app.MapChildrenModule();
app.MapVaccinesModule();
app.MapImmunizationsModule();
app.MapAppointmentsModule();
app.MapSyncModule();
app.MapNotificationsModule();
app.MapReportsModule();
app.MapAuditLogsModule();

app.Run();

static string[] GetAllowedCorsOrigins(IConfiguration configuration, IWebHostEnvironment environment)
{
    var configuredOrigins = configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>()
        ?? [];

    var environmentOrigins = configuration["CORS_ALLOWED_ORIGINS"]?
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ?? [];

    var origins = configuredOrigins
        .Concat(environmentOrigins)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (origins.Length > 0 || !environment.IsDevelopment())
    {
        return origins;
    }

    return
    [
        "http://localhost:3000",
        "https://localhost:3000",
        "http://localhost:5173",
        "https://localhost:5173"
    ];
}

public partial class Program;
