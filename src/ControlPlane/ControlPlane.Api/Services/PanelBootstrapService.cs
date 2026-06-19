using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Services;

public sealed class PanelBootstrapService(
    AppDbContext db,
    IConfiguration config,
    PanelPasswordHasher passwordHasher,
    ILogger<PanelBootstrapService> logger)
{
    public async Task EnsureBootstrapAdminAsync(CancellationToken ct)
    {
        var email = PanelAuthenticationService.NormalizeEmail(config["ControlPlane:BootstrapAdmin:Email"]);
        var password = config["ControlPlane:BootstrapAdmin:Password"]?.Trim();
        var displayName = string.IsNullOrWhiteSpace(config["ControlPlane:BootstrapAdmin:DisplayName"])
            ? "Administrador"
            : config["ControlPlane:BootstrapAdmin:DisplayName"]!.Trim();

        if (email is null || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("Bootstrap admin nao configurado. Nenhum usuario inicial sera criado automaticamente.");
            return;
        }

        var anyAdminExists = await db.PanelUsers.AnyAsync(
            u => u.IsActive && u.Role == PanelSecurityConstants.RoleAdmin,
            ct);
        if (anyAdminExists)
        {
            logger.LogInformation("Bootstrap admin ignorado porque ja existe um administrador ativo no banco.");
            return;
        }

        var existing = await db.PanelUsers.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (existing is not null)
        {
            existing.DisplayName = displayName;
            existing.Role = PanelSecurityConstants.RoleAdmin;
            existing.IsActive = true;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Usuario bootstrap existente promovido para administrador: {Email}", email);
            return;
        }

        var hash = passwordHasher.HashPassword(password);
        db.PanelUsers.Add(new PanelUser
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = email,
            DisplayName = displayName,
            Role = PanelSecurityConstants.RoleAdmin,
            PasswordHash = hash.HashBase64,
            PasswordSalt = hash.SaltBase64,
            PasswordIterations = hash.Iterations,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Administrador bootstrap criado com sucesso para {Email}", email);
    }
}
