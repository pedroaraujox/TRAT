using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Services;

public sealed class PanelAuthenticationService(
    AppDbContext db,
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
            return AuthenticatePanelUserResult.Failed("E-mail ou senha invalidos.");
        }

        var valid = passwordHasher.VerifyPassword(password, user.PasswordHash, user.PasswordSalt, user.PasswordIterations);
        if (!valid)
        {
            logger.LogWarning("Tentativa de login com senha invalida para user {UserId}", user.Id);
            return AuthenticatePanelUserResult.Failed("E-mail ou senha invalidos.");
        }

        user.LastLoginAtUtc = DateTimeOffset.UtcNow;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return AuthenticatePanelUserResult.Succeeded(new PanelUserSessionInfo(
            user.Id,
            user.Email,
            user.DisplayName,
            PanelSecurityConstants.NormalizeRole(user.Role)));
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
