using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Models;
using ControlPlane.Api.Security;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Controllers;

public sealed class PanelAdminController(
    AppDbContext db,
    PanelPasswordHasher passwordHasher,
    AgentPackageCatalogService agentPackageCatalogService) : Controller
{
    [HttpGet("/admin/panel")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var currentUser = HttpContext.GetCurrentPanelUser();
        if (currentUser is null)
        {
            return Redirect("/login");
        }

        var users = await db.PanelUsers.AsNoTracking().ToListAsync(ct);
        var package = agentPackageCatalogService.GetLatestPackage();

        return View("PanelAdmin", new PanelAdministrationViewModel
        {
            CurrentUser = currentUser,
            AgentDownload = MapDownload(package),
            UserCount = users.Count,
            ActiveUserCount = users.Count(u => u.IsActive),
            AdminUserCount = users.Count(u => u.IsActive && PanelSecurityConstants.IsAdminRole(u.Role))
        });
    }

    [HttpGet("/admin/panel/users")]
    public async Task<IActionResult> Users(CancellationToken ct)
    {
        var currentUser = HttpContext.GetCurrentPanelUser();
        if (currentUser is null)
        {
            return Redirect("/login");
        }

        var users = await db.PanelUsers.AsNoTracking()
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .ToListAsync(ct);

        return View("PanelUsers", new PanelUsersPageViewModel
        {
            CurrentUser = currentUser,
            Users = users.Select(MapUser).ToArray()
        });
    }

    [HttpGet("/admin/panel/users/new")]
    public IActionResult NewUser()
    {
        return View("PanelUserForm", new PanelUserFormViewModel
        {
            Email = string.Empty,
            DisplayName = string.Empty,
            Role = PanelSecurityConstants.RoleOperator,
            IsActive = true,
            Password = null,
            ConfirmPassword = null,
            IsEditMode = false
        });
    }

    [HttpPost("/admin/panel/users/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewUserPost([FromForm] PanelUserFormViewModel form, CancellationToken ct)
    {
        var normalized = NormalizeForm(form, isEditMode: false);
        var error = await ValidateFormAsync(normalized, isEditMode: false, currentUserId: null, ct);
        if (error is not null)
        {
            return View("PanelUserForm", normalized with { ErrorMessage = error });
        }

        var hash = passwordHasher.HashPassword(normalized.Password!);
        db.PanelUsers.Add(new PanelUser
        {
            Id = Guid.NewGuid().ToString("N"),
            Email = normalized.Email,
            DisplayName = normalized.DisplayName,
            Role = normalized.Role,
            PasswordHash = hash.HashBase64,
            PasswordSalt = hash.SaltBase64,
            PasswordIterations = hash.Iterations,
            IsActive = normalized.IsActive,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "Usuario criado com sucesso.";
        return Redirect("/admin/panel/users");
    }

    [HttpGet("/admin/panel/users/{id}/edit")]
    public async Task<IActionResult> EditUser(string id, CancellationToken ct)
    {
        var user = await db.PanelUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        return View("PanelUserForm", new PanelUserFormViewModel
        {
            OriginalId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = PanelSecurityConstants.NormalizeRole(user.Role),
            IsActive = user.IsActive,
            Password = null,
            ConfirmPassword = null,
            IsEditMode = true
        });
    }

    [HttpPost("/admin/panel/users/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUserPost(string id, [FromForm] PanelUserFormViewModel form, CancellationToken ct)
    {
        var normalized = NormalizeForm(form, isEditMode: true) with { OriginalId = id, IsEditMode = true };
        var error = await ValidateFormAsync(normalized, isEditMode: true, currentUserId: HttpContext.GetCurrentPanelUser()?.UserId, ct);
        if (error is not null)
        {
            return View("PanelUserForm", normalized with { ErrorMessage = error });
        }

        var user = await db.PanelUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound();
        }

        user.Email = normalized.Email;
        user.DisplayName = normalized.DisplayName;
        user.Role = normalized.Role;
        user.IsActive = normalized.IsActive;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(normalized.Password))
        {
            var hash = passwordHasher.HashPassword(normalized.Password);
            user.PasswordHash = hash.HashBase64;
            user.PasswordSalt = hash.SaltBase64;
            user.PasswordIterations = hash.Iterations;
        }

        await db.SaveChangesAsync(ct);

        var currentUser = HttpContext.GetCurrentPanelUser();
        if (currentUser is not null && string.Equals(currentUser.UserId, user.Id, StringComparison.Ordinal))
        {
            HttpContext.Session.SignInPanelUser(user.Id, user.Email, user.DisplayName, user.Role);
        }

        TempData["StatusMessage"] = "Usuario atualizado com sucesso.";
        return Redirect("/admin/panel/users");
    }

    [HttpPost("/admin/panel/users/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken ct)
    {
        var currentUser = HttpContext.GetCurrentPanelUser();
        if (currentUser is null)
        {
            return Redirect("/login");
        }

        var user = await db.PanelUsers.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            TempData["ErrorMessage"] = "Usuario nao encontrado.";
            return Redirect("/admin/panel/users");
        }

        if (string.Equals(currentUser.UserId, user.Id, StringComparison.Ordinal))
        {
            TempData["ErrorMessage"] = "Voce nao pode remover o proprio usuario logado.";
            return Redirect("/admin/panel/users");
        }

        if (PanelSecurityConstants.IsAdminRole(user.Role))
        {
            var otherActiveAdmins = await db.PanelUsers.CountAsync(
                u => u.IsActive && u.Id != id && u.Role == PanelSecurityConstants.RoleAdmin,
                ct);
            if (otherActiveAdmins == 0)
            {
                TempData["ErrorMessage"] = "Nao e permitido remover o ultimo administrador ativo.";
                return Redirect("/admin/panel/users");
            }
        }

        db.PanelUsers.Remove(user);
        await db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = "Usuario removido com sucesso.";
        return Redirect("/admin/panel/users");
    }

    [HttpGet("/admin/downloads")]
    public IActionResult Downloads()
    {
        var package = agentPackageCatalogService.GetLatestPackage();
        return View("Downloads", MapDownload(package));
    }

    [HttpGet("/admin/downloads/agent/setup")]
    public IActionResult DownloadAgentSetup()
    {
        var package = agentPackageCatalogService.GetLatestPackage();
        if (!package.IsAvailable || string.IsNullOrWhiteSpace(package.SetupExePath) || !System.IO.File.Exists(package.SetupExePath))
        {
            return NotFound();
        }

        return PhysicalFile(package.SetupExePath, "application/octet-stream", "WebstationBackup.Agent.Setup.exe");
    }

    [HttpGet("/admin/downloads/agent/zip")]
    public IActionResult DownloadAgentZip()
    {
        var package = agentPackageCatalogService.GetLatestPackage();
        if (!package.IsAvailable || string.IsNullOrWhiteSpace(package.ZipPath) || !System.IO.File.Exists(package.ZipPath))
        {
            return NotFound();
        }

        return PhysicalFile(package.ZipPath, "application/zip", "WebstationBackup.Agent.Package.zip");
    }

    private async Task<string?> ValidateFormAsync(PanelUserFormViewModel form, bool isEditMode, string? currentUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(form.Email))
        {
            return "E-mail e obrigatorio.";
        }

        if (string.IsNullOrWhiteSpace(form.DisplayName))
        {
            return "Nome do usuario e obrigatorio.";
        }

        if (!form.Email.Contains('@', StringComparison.Ordinal))
        {
            return "Informe um e-mail valido.";
        }

        if (string.IsNullOrWhiteSpace(form.Role))
        {
            return "Perfil do usuario e obrigatorio.";
        }

        if (!isEditMode && string.IsNullOrWhiteSpace(form.Password))
        {
            return "Senha e obrigatoria ao criar um usuario.";
        }

        if (!string.IsNullOrWhiteSpace(form.Password))
        {
            if (form.Password.Length < 8)
            {
                return "A senha deve ter ao menos 8 caracteres.";
            }

            if (!string.Equals(form.Password, form.ConfirmPassword, StringComparison.Ordinal))
            {
                return "Senha e confirmacao nao conferem.";
            }
        }

        var emailInUse = await db.PanelUsers.AnyAsync(
            u => u.Email == form.Email && (!isEditMode || u.Id != form.OriginalId),
            ct);
        if (emailInUse)
        {
            return "Ja existe um usuario com este e-mail.";
        }

        if (isEditMode && string.Equals(currentUserId, form.OriginalId, StringComparison.Ordinal) && !form.IsActive)
        {
            return "Voce nao pode desativar o proprio usuario logado.";
        }

        if (isEditMode && string.Equals(currentUserId, form.OriginalId, StringComparison.Ordinal) && !PanelSecurityConstants.IsAdminRole(form.Role))
        {
            return "Voce nao pode remover o proprio perfil de administrador nesta operacao.";
        }

        if (!form.IsActive || !PanelSecurityConstants.IsAdminRole(form.Role))
        {
            var current = await db.PanelUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == form.OriginalId, ct);
            if (current is not null && PanelSecurityConstants.IsAdminRole(current.Role) && current.IsActive)
            {
                var otherActiveAdmins = await db.PanelUsers.CountAsync(
                    u => u.IsActive && u.Id != current.Id && u.Role == PanelSecurityConstants.RoleAdmin,
                    ct);
                if (otherActiveAdmins == 0)
                {
                    return "Nao e permitido desativar ou rebaixar o ultimo administrador ativo.";
                }
            }
        }

        return null;
    }

    private static PanelUserFormViewModel NormalizeForm(PanelUserFormViewModel form, bool isEditMode)
    {
        return form with
        {
            Email = PanelAuthenticationService.NormalizeEmail(form.Email) ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(form.DisplayName) ? string.Empty : form.DisplayName.Trim(),
            Role = PanelSecurityConstants.NormalizeRole(form.Role),
            Password = string.IsNullOrWhiteSpace(form.Password) ? null : form.Password.Trim(),
            ConfirmPassword = string.IsNullOrWhiteSpace(form.ConfirmPassword) ? null : form.ConfirmPassword.Trim(),
            IsEditMode = isEditMode
        };
    }

    private static PanelUserListItemViewModel MapUser(PanelUser user)
    {
        return new PanelUserListItemViewModel
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = PanelSecurityConstants.NormalizeRole(user.Role),
            IsActive = user.IsActive,
            CreatedAtUtc = user.CreatedAtUtc,
            UpdatedAtUtc = user.UpdatedAtUtc,
            LastLoginAtUtc = user.LastLoginAtUtc
        };
    }

    private static AgentDownloadViewModel MapDownload(AgentPackageCatalogResult package)
    {
        return new AgentDownloadViewModel
        {
            IsAvailable = package.IsAvailable,
            Message = package.Message,
            SearchRoot = package.SearchRoot,
            Version = package.Version,
            PublishedAtUtc = package.PublishedAtUtc,
            HasSetupExe = !string.IsNullOrWhiteSpace(package.SetupExePath),
            HasZip = !string.IsNullOrWhiteSpace(package.ZipPath)
        };
    }
}
