using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Security;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ControlPlane.Api.Services;

public sealed class PanelBootstrapService(
    AppDbContext db,
    IConfiguration config,
    IWebHostEnvironment env,
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
            TryRemoveBootstrapPasswordFromLocalConfig();
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
            TryRemoveBootstrapPasswordFromLocalConfig();
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
        TryRemoveBootstrapPasswordFromLocalConfig();
    }

    private void TryRemoveBootstrapPasswordFromLocalConfig()
    {
        var localConfigPath = Path.Combine(env.ContentRootPath, "appsettings.Local.json");
        if (!File.Exists(localConfigPath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(localConfigPath);
            var root = JsonNode.Parse(json) as JsonObject;
            if (root is null)
            {
                return;
            }

            var controlPlane = root["ControlPlane"] as JsonObject;
            var bootstrap = controlPlane?["BootstrapAdmin"] as JsonObject;
            if (bootstrap is null)
            {
                return;
            }

            if (!bootstrap.ContainsKey("Password"))
            {
                return;
            }

            bootstrap.Remove("Password");

            var backupPath = localConfigPath + ".bak-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            File.Copy(localConfigPath, backupPath, overwrite: true);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(localConfigPath, root.ToJsonString(options));
            logger.LogInformation("Senha bootstrap removida de appsettings.Local.json apos bootstrap do administrador.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao remover senha bootstrap de appsettings.Local.json.");
        }
    }
}
