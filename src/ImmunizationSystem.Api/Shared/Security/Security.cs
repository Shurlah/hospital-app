using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using ImmunizationSystem.Api.Shared.Database;
using Microsoft.IdentityModel.Tokens;

namespace ImmunizationSystem.Api.Shared.Security;

public static class RoleNames
{
    public const string SystemAdministrator = "SystemAdministrator";
    public const string LgaHealthOfficial = "LgaHealthOfficial";
    public const string FacilitySupervisor = "FacilitySupervisor";
    public const string HealthWorker = "HealthWorker";
    public const string Auditor = "Auditor";
}

public static class AuthPolicies
{
    public const string SystemAdminOnly = "SystemAdminOnly";
    public const string CanManageUsers = "CanManageUsers";
    public const string CanManageFacilities = "CanManageFacilities";
    public const string CanViewReports = "CanViewReports";
    public const string CanRecordImmunization = "CanRecordImmunization";
    public const string CanSyncDevice = "CanSyncDevice";
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; init; } = "ImmunizationSystem";
    public string Audience { get; init; } = "ImmunizationSystem.Clients";
    public string SigningKey { get; init; } = "development-signing-key-change-before-deployment-32chars";
    public int AccessTokenMinutes { get; init; } = 30;
    public int RefreshTokenDays { get; init; } = 14;
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

public sealed class BCryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}

public interface ITokenService
{
    string CreateAccessToken(User user, string roleName);

    (string Token, string TokenHash, DateTime ExpiresAt) CreateRefreshToken();
}

public sealed class JwtTokenService(IConfiguration configuration) : ITokenService
{
    public string CreateAccessToken(User user, string roleName)
    {
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email),
            new(ClaimTypes.Role, roleName),
            new("facility_id", user.FacilityId?.ToString() ?? string.Empty)
        };

        var token = new JwtSecurityToken(
            options.Issuer,
            options.Audience,
            claims,
            expires: DateTime.UtcNow.AddMinutes(options.AccessTokenMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string Token, string TokenHash, DateTime ExpiresAt) CreateRefreshToken()
    {
        var options = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var tokenBytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(tokenBytes);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return (token, hash, DateTime.UtcNow.AddDays(options.RefreshTokenDays));
    }
}

public static class UserContext
{
    public static Guid? GetUserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    public static Guid? GetFacilityId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue("facility_id"), out var facilityId) ? facilityId : null;
}
