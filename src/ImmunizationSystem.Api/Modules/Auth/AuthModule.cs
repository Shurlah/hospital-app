using System.Net;
using System.Security.Cryptography;
using System.Text;
using ImmunizationSystem.Api.Shared.Cqrs;
using ImmunizationSystem.Api.Shared.Database;
using ImmunizationSystem.Api.Shared.Errors;
using ImmunizationSystem.Api.Shared.Security;
using Microsoft.EntityFrameworkCore;

namespace ImmunizationSystem.Api.Modules.Auth;

public static class AuthModule
{
    public static IEndpointRouteBuilder MapAuthModule(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginCommand command, IRequestDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(await dispatcher.SendAsync(command, ct)));

        group.MapPost("/refresh-token", async (RefreshTokenCommand command, IRequestDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(await dispatcher.SendAsync(command, ct)));

        group.MapPost("/logout", async (LogoutCommand command, IRequestDispatcher dispatcher, CancellationToken ct) =>
            Results.Ok(await dispatcher.SendAsync(command, ct))).RequireAuthorization();

        return app;
    }
}

public sealed record LoginCommand(string Email, string Password) : ICommand<AuthResponse>;

public sealed record RefreshTokenCommand(string RefreshToken) : ICommand<AuthResponse>;

public sealed record LogoutCommand(string RefreshToken) : ICommand<object>;

public sealed record AuthResponse(string AccessToken, string RefreshToken, Guid UserId, string Role, Guid? FacilityId);

public sealed class LoginHandler(
    ApplicationDbContext dbContext,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IHttpContextAccessor httpContextAccessor)
    : ICommandHandler<LoginCommand, AuthResponse>
{
    public async Task<AuthResponse> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.Include(x => x.Role)
            .SingleOrDefaultAsync(x => x.Email == command.Email, cancellationToken);

        if (user is null || !user.IsActive || !passwordHasher.Verify(command.Password, user.PasswordHash))
        {
            await AuditAsync(null, "Login failure", cancellationToken);
            throw new ApiException("UNAUTHORIZED", "Invalid credentials.", HttpStatusCode.Unauthorized);
        }

        var refresh = tokenService.CreateRefreshToken();
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.TokenHash,
            ExpiresAt = refresh.ExpiresAt
        });

        await AuditAsync(user.Id, "Login success", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(user, user.Role?.Name ?? string.Empty),
            refresh.Token,
            user.Id,
            user.Role?.Name ?? string.Empty,
            user.FacilityId);
    }

    private Task AuditAsync(Guid? userId, string action, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = "Auth",
            IpAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
        });
        return Task.CompletedTask;
    }
}

public sealed class RefreshTokenHandler(ApplicationDbContext dbContext, ITokenService tokenService)
    : ICommandHandler<RefreshTokenCommand, AuthResponse>
{
    public async Task<AuthResponse> HandleAsync(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = Hash(command.RefreshToken);
        var refreshToken = await dbContext.RefreshTokens.Include(x => x.User).ThenInclude(x => x!.Role)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

        if (refreshToken is null || refreshToken.UsedAt is not null || refreshToken.RevokedAt is not null ||
            refreshToken.ExpiresAt <= DateTime.UtcNow || refreshToken.User is null || !refreshToken.User.IsActive)
        {
            throw new ApiException("UNAUTHORIZED", "Invalid refresh token.", HttpStatusCode.Unauthorized);
        }

        refreshToken.UsedAt = DateTime.UtcNow;
        var replacement = tokenService.CreateRefreshToken();
        dbContext.RefreshTokens.Add(new RefreshToken
        {
            UserId = refreshToken.UserId,
            TokenHash = replacement.TokenHash,
            ExpiresAt = replacement.ExpiresAt
        });
        dbContext.AuditLogs.Add(new AuditLog
        {
            UserId = refreshToken.UserId,
            Action = "Refresh token rotated",
            EntityType = "Auth"
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthResponse(
            tokenService.CreateAccessToken(refreshToken.User, refreshToken.User.Role?.Name ?? string.Empty),
            replacement.Token,
            refreshToken.UserId,
            refreshToken.User.Role?.Name ?? string.Empty,
            refreshToken.User.FacilityId);
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed class LogoutHandler(ApplicationDbContext dbContext) : ICommandHandler<LogoutCommand, object>
{
    public async Task<object> HandleAsync(LogoutCommand command, CancellationToken cancellationToken)
    {
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.RefreshToken)));
        var refreshToken = await dbContext.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
        if (refreshToken is not null)
        {
            refreshToken.RevokedAt = DateTime.UtcNow;
            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = refreshToken.UserId,
                Action = "Logout",
                EntityType = "Auth"
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new { message = "Logged out." };
    }
}
