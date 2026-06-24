using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Services;

public sealed class PanelAuthenticationService(
    AppDbContext db,
    IConfiguration config,
    PanelPasswordHasher passwordHasher,
    ILogger<PanelAuthenticationService> logger)
{
    public async Task<AuthenticatePanelUserResult> AuthenticateAsync(string? email, string? password, CancellationToken ct)
    {
        var normalizedEmail = NormalizeEmail(email);
        if (normalizedEmail is null || string.IsNullOrWhiteSpace(password))
        {
            return AuthenticatePanelUserResult.Failed("Informe e-mail e senha.");
        }

        var user = await db.PanelUsers.FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);
        if (user is null || !user.IsActive)
        {
            logger.LogWarning("Tentativa de login negada para email {Email}", normalizedEmail);
            await ApplyFailureDelayAsync(ct);
            return AuthenticatePanelUserResult.Failed("E-mail ou senha invalidos.");
        }

        var nowUtc = DateTimeOffset.UtcNow;
        if (user.LockoutUntilUtc is not null && user.LockoutUntilUtc.Value > nowUtc)
        {
            logger.LogWarning("Tentativa de login em conta bloqueada. user={UserId}", user.Id);
            await ApplyFailureDelayAsync(ct);
            return AuthenticatePanelUserResult.Failed("Conta temporariamente bloqueada. Tente novamente em alguns minutos.");
        }

        var valid = passwordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations);
        if (!valid)
        {
            logger.LogWarning("Tentativa de login com senha invalida para user {UserId}", user.Id);
            await RegisterFailedLoginAsync(user, nowUtc, ct);
            await ApplyFailureDelayAsync(ct);
            return AuthenticatePanelUserResult.Failed("E-mail ou senha invalidos.");
        }

        user.FailedLoginCount = 0;
        user.LastFailedLoginAtUtc = null;
        user.LockoutUntilUtc = null;
        user.LastLoginAtUtc = nowUtc;
        user.UpdatedAtUtc = nowUtc;
        await db.SaveChangesAsync(ct);

        return AuthenticatePanelUserResult.Succeeded(new PanelUserSessionInfo(
            user.Id,
            user.Email,
            user.DisplayName,
            PanelSecurityConstants.NormalizeRole(user.Role)));
    }

    private async Task RegisterFailedLoginAsync(PanelUser user, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var windowMinutes = config.GetValue("ControlPlane:Security:PanelLockout:WindowMinutes", 15);
        var maxFailedAttempts = config.GetValue("ControlPlane:Security:PanelLockout:MaxFailedAttempts", 5);
        var lockoutMinutes = config.GetValue("ControlPlane:Security:PanelLockout:LockoutMinutes", 15);

        if (user.LastFailedLoginAtUtc is null || user.LastFailedLoginAtUtc.Value < nowUtc.AddMinutes(-windowMinutes))
        {
            user.FailedLoginCount = 0;
        }

        user.FailedLoginCount += 1;
        user.LastFailedLoginAtUtc = nowUtc;
        user.UpdatedAtUtc = nowUtc;

        if (user.FailedLoginCount >= maxFailedAttempts)
        {
            user.LockoutUntilUtc = nowUtc.AddMinutes(lockoutMinutes);
        }

        await db.SaveChangesAsync(ct);
    }

    private static Task ApplyFailureDelayAsync(CancellationToken ct)
    {
        return Task.Delay(TimeSpan.FromMilliseconds(250), ct);
    }

    public static string? NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim().ToLowerInvariant();
    }
}

public sealed record PanelUserSessionInfo(string UserId, string Email, string DisplayName, string Role);

public sealed record AuthenticatePanelUserResult(bool Success, string? ErrorMessage, PanelUserSessionInfo? Session)
{
    public static AuthenticatePanelUserResult Failed(string message)
        => new(false, message, null);

    public static AuthenticatePanelUserResult Succeeded(PanelUserSessionInfo session)
        => new(true, null, session);
}
