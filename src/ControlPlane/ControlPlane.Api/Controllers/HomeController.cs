using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Models;
using ControlPlane.Api.Security;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace ControlPlane.Api.Controllers;

public sealed class HomeController(
    AppDbContext db,
    AwsDiscoveryService awsDiscoveryService,
    PanelAuthenticationService panelAuthenticationService,
    HostOperationalStatusService hostOperationalStatusService,
    AuditTrailService auditTrailService,
    ILogger<HomeController> logger) : Controller
{
    private const string EnrollmentTokenSessionKeyPrefix = "customer-enroll-token:";

    [HttpGet("/")]
    public IActionResult Root() => Redirect("/admin");

    [HttpGet("/login")]
    public IActionResult Login() => View(new LoginViewModel());

    [HttpPost("/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoginPost([FromForm] string? email, [FromForm] string? password, CancellationToken ct)
    {
        var result = await panelAuthenticationService.AuthenticateAsync(email, password, ct);
        if (!result.Success || result.Session is null)
        {
            await auditTrailService.RecordAsync(
                HttpContext,
                category: "auth",
                action: "login",
                entityType: "panel_user",
                entityId: null,
                message: "Tentativa de login negada no painel.",
                customerId: null,
                hostId: null,
                outcome: "failure",
                metadata: new Dictionary<string, string?>
                {
                    ["email"] = PanelAuthenticationService.NormalizeEmail(email)
                },
                ct);
            return View("Login", new LoginViewModel
            {
                Email = email?.Trim(),
                ErrorMessage = result.ErrorMessage ?? "Falha ao autenticar."
            });
        }

        HttpContext.Session.SignInPanelUser(
            result.Session.UserId,
            result.Session.Email,
            result.Session.DisplayName,
            result.Session.Role);
        await auditTrailService.RecordAsync(
            HttpContext,
            category: "auth",
            action: "login",
            entityType: "panel_user",
            entityId: result.Session.UserId,
            message: "Login realizado com sucesso no painel.",
            customerId: null,
            hostId: null,
            outcome: "success",
            metadata: new Dictionary<string, string?>
            {
                ["email"] = result.Session.Email,
                ["role"] = result.Session.Role
            },
            ct);
        return Redirect("/admin/customers");
    }

    [HttpPost("/logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var currentUser = HttpContext.GetCurrentPanelUser();
        if (currentUser is not null)
        {
            await auditTrailService.RecordAsync(
                HttpContext,
                category: "auth",
                action: "logout",
                entityType: "panel_user",
                entityId: currentUser.UserId,
                message: "Logout realizado no painel.",
                customerId: null,
                hostId: null,
                outcome: "success",
                metadata: new Dictionary<string, string?>
                {
                    ["email"] = currentUser.Email,
                    ["role"] = currentUser.Role
                },
                ct);
        }

        HttpContext.Session.SignOutPanelUser();
        return Redirect("/login");
    }

    [HttpGet("/admin")]
    public IActionResult Dashboard()
    {
        return Redirect("/admin/customers");
    }

    [HttpGet("/admin/dashboard-legacy")]
    public async Task<IActionResult> DashboardLegacy(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var customers = await db.Customers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        var customersById = customers.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var policiesById = await db.BackupPolicies.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p, ct);
        var allConfigurations = await db.AgentConfigurations.AsNoTracking().ToListAsync(ct);
        var configsByHostId = allConfigurations
            .GroupBy(c => c.HostId)
            .Select(g => g.OrderByDescending(x => x.LastConfigSyncAtUtc ?? x.CreatedAtUtc).FirstOrDefault()!)
            .ToDictionary(c => c.HostId, c => c, StringComparer.OrdinalIgnoreCase);
        var allHosts = await db.Hosts.AsNoTracking().ToListAsync(ct);
        var hosts = allHosts
            .OrderByDescending(h => h.LastHeartbeatAtUtc)
            .Take(20)
            .ToList();
        var allHostsById = allHosts.ToDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);
        var hostsById = hosts.ToDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);
        var jobs = (await db.Jobs.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(j => j.StartedAtUtc)
            .Take(20)
            .ToList();
        var allJobs = await db.Jobs.AsNoTracking().ToListAsync(ct);
        var allAlerts = await db.Alerts.AsNoTracking().ToListAsync(ct);
        var alerts = allAlerts
            .Where(a => a.ResolvedAtUtc == null)
            .OrderByDescending(a => a.LastObservedAtUtc)
            .Take(20)
            .ToList();
        var hostCounts = await db.Hosts.AsNoTracking()
            .GroupBy(h => h.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count, ct);
        var failedCounts = allJobs
            .Where(j => j.State == "FAILED" && j.StartedAtUtc >= DateTimeOffset.UtcNow.AddDays(-30))
            .GroupBy(j => j.CustomerId)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var lastJobByCustomer = allJobs
            .GroupBy(j => j.CustomerId)
            .Select(g => new { CustomerId = g.Key, LastJobAtUtc = g.Max(x => x.StartedAtUtc) })
            .ToDictionary(x => x.CustomerId, x => (DateTimeOffset?)x.LastJobAtUtc, StringComparer.OrdinalIgnoreCase);
        var pendingRunRequestCount = await db.AgentRunRequests.AsNoTracking()
            .CountAsync(r => r.State == "QUEUED" || r.State == "CLAIMED", ct);
        var recentAuditEvents = (await db.AuditEvents.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(12)
            .ToList();
        var latestJobsByHostId = allJobs
            .GroupBy(j => j.HostId)
            .Select(g => g.OrderByDescending(x => x.StartedAtUtc).First())
            .ToDictionary(j => j.HostId, j => j, StringComparer.OrdinalIgnoreCase);
        var health = BuildDashboardHealthSummary(nowUtc, allHosts, configsByHostId.Values, pendingRunRequestCount);
        var operationalRisks = BuildOperationalRisks(
            nowUtc,
            allHosts,
            configsByHostId,
            latestJobsByHostId,
            customersById);

        var customerCards = customers
            .Select(c => new CustomerCardViewModel
            {
                Id = c.Id,
                Name = c.Name,
                AwsAccountId = c.AwsAccountId,
                HostCount = hostCounts.GetValueOrDefault(c.Id, 0),
                FailedJobsLast30Days = failedCounts.GetValueOrDefault(c.Id, 0),
                LastJobAtUtc = lastJobByCustomer.GetValueOrDefault(c.Id)
            })
            .ToArray();

        var model = new DashboardViewModel
        {
            Summary = new DashboardSummary
            {
                CustomerCount = await db.Customers.CountAsync(ct),
                HostCount = await db.Hosts.CountAsync(ct),
                ActiveAlertCount = await db.Alerts.CountAsync(a => a.ResolvedAtUtc == null, ct),
                FailedJobsLast7Days = allJobs.Count(j => j.State == "FAILED" && j.StartedAtUtc >= nowUtc.AddDays(-7)),
                SuccessfulJobsLast24Hours = allJobs.Count(j => j.State == "SUCCEEDED" && j.StartedAtUtc >= nowUtc.AddHours(-24))
            },
            Health = health,
            AlertAnalytics = BuildAlertAnalyticsSummary(allAlerts, customersById, allHostsById),
            Customers = customerCards,
            Hosts = hosts.Select(h => MapHost(h, customersById, configsByHostId, policiesById)).ToArray(),
            RecentJobs = jobs.Select(j => MapJob(j, customersById, hostsById)).ToArray(),
            Alerts = alerts.Select(a => MapAlert(a, customersById, allHostsById, configsByHostId, latestJobsByHostId)).ToArray(),
            OperationalRisks = operationalRisks,
            RecentAuditEvents = recentAuditEvents.Select(MapAuditEvent).ToArray()
        };

        return View("Dashboard", model);
    }

    [HttpGet("/admin/customers")]
    public async Task<IActionResult> Customers([FromQuery] string? q, CancellationToken ct)
    {
        var query = db.Customers.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(c => c.Id.Contains(term) || c.Name.Contains(term) || c.AwsAccountId.Contains(term));
        }

        var customers = await query.OrderBy(c => c.Name).ToListAsync(ct);
        var hostCounts = await db.Hosts.AsNoTracking()
            .GroupBy(h => h.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count, ct);
        var jobCounts = await db.Jobs.AsNoTracking()
            .GroupBy(j => j.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count, ct);
        var alertCounts = await db.Alerts.AsNoTracking()
            .Where(a => a.ResolvedAtUtc == null)
            .GroupBy(a => a.CustomerId)
            .Select(g => new { CustomerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CustomerId, x => x.Count, ct);

        return View(new CustomersPageViewModel
        {
            Search = q,
            Customers = customers.Select(c => new CustomerListItemViewModel
            {
                Id = c.Id,
                Name = c.Name,
                AwsAccountId = c.AwsAccountId,
                NotificationEmailsCsv = c.NotificationEmailsCsv,
                CreatedAtUtc = c.CreatedAtUtc,
                HostCount = hostCounts.GetValueOrDefault(c.Id, 0),
                JobCount = jobCounts.GetValueOrDefault(c.Id, 0),
                ActiveAlertCount = alertCounts.GetValueOrDefault(c.Id, 0)
            }).ToArray()
        });
    }

    [HttpGet("/admin/customers/new")]
    public IActionResult NewCustomer()
    {
        return View("CustomerForm", new CustomerFormViewModel
        {
            Id = string.Empty,
            Name = string.Empty,
            AwsAccountId = string.Empty,
            NotificationEmailsCsv = string.Empty,
            IsEditMode = false
        });
    }

    [HttpPost("/admin/customers/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCustomer([FromForm] CustomerFormViewModel form, CancellationToken ct)
    {
        var validationError = ValidateCustomerForm(form, isEditMode: false);
        if (validationError is not null)
        {
            return View("CustomerForm", form with { ErrorMessage = validationError, IsEditMode = false });
        }

        var exists = await db.Customers.AnyAsync(c => c.Id == form.Id.Trim(), ct);
        if (exists)
        {
            return View("CustomerForm", form with { ErrorMessage = "Ja existe um cliente com este ID.", IsEditMode = false });
        }

        var customerId = form.Id.Trim();
        var enrollmentToken = GenerateEnrollmentToken();
        var enrollmentTokenHash = ComputeTokenHash(enrollmentToken);

        db.Customers.Add(new Customer
        {
            Id = customerId,
            Name = form.Name.Trim(),
            AwsAccountId = form.AwsAccountId.Trim(),
            NotificationEmailsCsv = string.IsNullOrWhiteSpace(form.NotificationEmailsCsv) ? null : form.NotificationEmailsCsv.Trim(),
            AgentEnrollmentTokenHash = enrollmentTokenHash,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(
            category: "customer",
            action: "create",
            entityType: "customer",
            entityId: customerId,
            message: $"Cliente {customerId} criado no painel.",
            customerId: customerId,
            hostId: null,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["name"] = form.Name.Trim(),
                ["awsAccountId"] = form.AwsAccountId.Trim()
            });

        HttpContext.Session.SetString(BuildEnrollmentTokenSessionKey(customerId), enrollmentToken);
        return Redirect($"/admin/customers/{Uri.EscapeDataString(customerId)}");
    }

    [HttpGet("/admin/customers/{id}")]
    public async Task<IActionResult> CustomerDetail(string id, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            return NotFound();
        }

        var tokenSessionKey = BuildEnrollmentTokenSessionKey(customer.Id);
        var enrollmentToken = HttpContext.Session.GetString(tokenSessionKey);
        if (!string.IsNullOrWhiteSpace(enrollmentToken))
        {
            HttpContext.Session.Remove(tokenSessionKey);
        }

        var customerMap = new Dictionary<string, Customer>(StringComparer.OrdinalIgnoreCase) { [customer.Id] = customer };
        var policyMap = await db.BackupPolicies.AsNoTracking()
            .Where(p => p.CustomerId == id)
            .ToDictionaryAsync(p => p.Id, p => p, ct);
        var configsByHostId = (await db.AgentConfigurations.AsNoTracking()
            .Where(c => c.CustomerId == id)
            .ToListAsync(ct))
            .GroupBy(c => c.HostId)
            .Select(g => g.OrderByDescending(x => x.LastConfigSyncAtUtc ?? x.CreatedAtUtc).FirstOrDefault()!)
            .ToDictionary(c => c.HostId, c => c, StringComparer.OrdinalIgnoreCase);
        var hosts = (await db.Hosts.AsNoTracking().Where(h => h.CustomerId == id).ToListAsync(ct))
            .OrderByDescending(h => h.LastHeartbeatAtUtc)
            .ToList();
        var hostsById = hosts.ToDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);
        var jobs = (await db.Jobs.AsNoTracking().Where(j => j.CustomerId == id).ToListAsync(ct))
            .OrderByDescending(j => j.StartedAtUtc)
            .Take(30)
            .ToList();
        var alerts = (await db.Alerts.AsNoTracking().Where(a => a.CustomerId == id).ToListAsync(ct))
            .OrderByDescending(a => a.LastObservedAtUtc)
            .Take(30)
            .ToList();
        var allCustomerAlerts = await db.Alerts.AsNoTracking()
            .Where(a => a.CustomerId == id)
            .ToListAsync(ct);
        var policies = await db.BackupPolicies.AsNoTracking()
            .Where(p => p.CustomerId == id)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
        return View("CustomerDetail", new CustomerDetailViewModel
        {
            Customer = new CustomerFormViewModel
            {
                OriginalId = customer.Id,
                Id = customer.Id,
                Name = customer.Name,
                AwsAccountId = customer.AwsAccountId,
                NotificationEmailsCsv = customer.NotificationEmailsCsv,
                IsEditMode = true
            },
            EnrollmentTokenOneTime = string.IsNullOrWhiteSpace(enrollmentToken) ? null : enrollmentToken,
            AlertAnalytics = BuildAlertAnalyticsSummary(allCustomerAlerts, customerMap, hostsById),
            Hosts = hosts.Select(h => MapHost(h, customerMap, configsByHostId, policyMap)).ToArray(),
            Jobs = jobs.Select(j => MapJob(j, customerMap, hostsById)).ToArray(),
            Alerts = alerts.Select(a => MapAlert(
                a,
                customerMap,
                hostsById,
                configsByHostId,
                jobs.GroupBy(j => j.HostId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartedAtUtc).First(), StringComparer.OrdinalIgnoreCase))).ToArray(),
            Policies = policies.Select(p => MapPolicy(p, customerMap, hostsById)).ToArray()
        });
    }

    [HttpPost("/admin/customers/{id}/token/regenerate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RegenerateCustomerEnrollmentToken(string id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest();
        }

        var customerId = id.Trim();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
        {
            return NotFound();
        }

        var enrollmentToken = GenerateEnrollmentToken();
        customer.AgentEnrollmentTokenHash = ComputeTokenHash(enrollmentToken);
        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(
            category: "customer",
            action: "regenerate_token",
            entityType: "customer",
            entityId: customerId,
            message: $"Token de enrollment regenerado para o cliente {customerId}.",
            customerId: customerId,
            hostId: null,
            ct);

        HttpContext.Session.SetString(BuildEnrollmentTokenSessionKey(customerId), enrollmentToken);
        return Redirect($"/admin/customers/{Uri.EscapeDataString(customerId)}");
    }

    private static string BuildEnrollmentTokenSessionKey(string customerId)
    {
        return EnrollmentTokenSessionKeyPrefix + customerId.Trim().ToLowerInvariant();
    }

    private void TryRemoveEnrollmentTokenFromSession(string customerId)
    {
        var session = HttpContext.Features.Get<ISessionFeature>()?.Session;
        if (session is null)
        {
            return;
        }

        session.Remove(BuildEnrollmentTokenSessionKey(customerId));
    }

    private static string GenerateEnrollmentToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var base64 = Convert.ToBase64String(bytes);
        return base64.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string ComputeTokenHash(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    [HttpGet("/admin/customers/{id}/edit")]
    public async Task<IActionResult> EditCustomer(string id, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            return NotFound();
        }

        return View("CustomerForm", new CustomerFormViewModel
        {
            OriginalId = customer.Id,
            Id = customer.Id,
            Name = customer.Name,
            AwsAccountId = customer.AwsAccountId,
            NotificationEmailsCsv = customer.NotificationEmailsCsv,
            IsEditMode = true
        });
    }

    [HttpPost("/admin/customers/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCustomerPost(string id, [FromForm] CustomerFormViewModel form, CancellationToken ct)
    {
        var validationError = ValidateCustomerForm(form, isEditMode: true);
        if (validationError is not null)
        {
            return View("CustomerForm", form with { ErrorMessage = validationError, IsEditMode = true, OriginalId = id });
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null)
        {
            return NotFound();
        }

        customer.Name = form.Name.Trim();
        customer.AwsAccountId = form.AwsAccountId.Trim();
        customer.NotificationEmailsCsv = string.IsNullOrWhiteSpace(form.NotificationEmailsCsv) ? null : form.NotificationEmailsCsv.Trim();

        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(
            category: "customer",
            action: "edit",
            entityType: "customer",
            entityId: id,
            message: $"Cliente {id} atualizado no painel.",
            customerId: id,
            hostId: null,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["name"] = customer.Name,
                ["awsAccountId"] = customer.AwsAccountId
            });
        return Redirect($"/admin/customers/{Uri.EscapeDataString(id)}");
    }

    [HttpPost("/admin/customers/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCustomer(string id, [FromForm] string? returnUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            TempData["ErrorMessage"] = "Cliente invalido.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/customers");
        }

        var customerId = id.Trim();
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
        {
            TempData["ErrorMessage"] = "Cliente nao encontrado.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/customers");
        }

        var jobIds = await db.Jobs
            .Where(j => j.CustomerId == customerId)
            .Select(j => j.Id)
            .ToListAsync(ct);
        var policyIds = await db.BackupPolicies
            .Where(p => p.CustomerId == customerId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        var artifacts = jobIds.Count == 0
            ? []
            : await db.Artifacts.Where(a => jobIds.Contains(a.JobId)).ToListAsync(ct);
        var jobs = jobIds.Count == 0
            ? []
            : await db.Jobs.Where(j => j.CustomerId == customerId).ToListAsync(ct);
        var alerts = await db.Alerts.Where(a => a.CustomerId == customerId).ToListAsync(ct);
        var configurations = await db.AgentConfigurations.Where(c => c.CustomerId == customerId).ToListAsync(ct);
        var runRequests = await db.AgentRunRequests.Where(r => r.CustomerId == customerId).ToListAsync(ct);
        var policyChangeEvents = policyIds.Count == 0
            ? []
            : await db.PolicyChangeEvents.Where(e => policyIds.Contains(e.PolicyId)).ToListAsync(ct);
        var policies = policyIds.Count == 0
            ? []
            : await db.BackupPolicies.Where(p => p.CustomerId == customerId).ToListAsync(ct);
        var hosts = await db.Hosts.Where(h => h.CustomerId == customerId).ToListAsync(ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            if (artifacts.Count > 0)
            {
                db.Artifacts.RemoveRange(artifacts);
            }

            if (alerts.Count > 0)
            {
                db.Alerts.RemoveRange(alerts);
            }

            if (configurations.Count > 0)
            {
                db.AgentConfigurations.RemoveRange(configurations);
            }

            if (runRequests.Count > 0)
            {
                db.AgentRunRequests.RemoveRange(runRequests);
            }

            if (policyChangeEvents.Count > 0)
            {
                db.PolicyChangeEvents.RemoveRange(policyChangeEvents);
            }

            if (policies.Count > 0)
            {
                db.BackupPolicies.RemoveRange(policies);
            }

            if (jobs.Count > 0)
            {
                db.Jobs.RemoveRange(jobs);
            }

            if (hosts.Count > 0)
            {
                db.Hosts.RemoveRange(hosts);
            }

            db.Customers.Remove(customer);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            logger.LogError(ex, "Customer {CustomerId} deletion failed", customerId);
            TempData["ErrorMessage"] = "Falha ao excluir o cliente.";
            return RedirectToLocalOrDefault(returnUrl, $"/admin/customers/{Uri.EscapeDataString(customerId)}");
        }

        TryRemoveEnrollmentTokenFromSession(customerId);
        logger.LogInformation(
            "Customer {CustomerId} deleted with dependencies. Hosts={HostCount}, Jobs={JobCount}, Alerts={AlertCount}, Configurations={ConfigurationCount}, RunRequests={RunRequestCount}, Policies={PolicyCount}, PolicyEvents={PolicyEventCount}, Artifacts={ArtifactCount}",
            customerId,
            hosts.Count,
            jobs.Count,
            alerts.Count,
            configurations.Count,
            runRequests.Count,
            policies.Count,
            policyChangeEvents.Count,
            artifacts.Count);
        await RecordAuditAsync(
            category: "customer",
            action: "delete",
            entityType: "customer",
            entityId: customerId,
            message: $"Cliente {customerId} removido com dependencias relacionadas.",
            customerId: customerId,
            hostId: null,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["hosts"] = hosts.Count.ToString(),
                ["jobs"] = jobs.Count.ToString(),
                ["alerts"] = alerts.Count.ToString(),
                ["policies"] = policies.Count.ToString()
            });

        TempData["StatusMessage"] = "Cliente excluido com sucesso.";
        return RedirectToLocalOrDefault(returnUrl, "/admin/customers");
    }

    [HttpGet("/admin/hosts")]
    public async Task<IActionResult> Hosts([FromQuery] string? customerId, [FromQuery] string? status, [FromQuery] string? recoveryStatus, CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var policies = await db.BackupPolicies.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p, ct);
        var configsByHostId = (await db.AgentConfigurations.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.HostId)
            .Select(g => g.OrderByDescending(x => x.LastConfigSyncAtUtc ?? x.CreatedAtUtc).FirstOrDefault()!)
            .ToDictionary(c => c.HostId, c => c, StringComparer.OrdinalIgnoreCase);
        var latestJobByHostId = (await db.Jobs.AsNoTracking().ToListAsync(ct))
            .GroupBy(j => j.HostId)
            .Select(g => g.OrderByDescending(j => j.StartedAtUtc).FirstOrDefault()!)
            .ToDictionary(j => j.HostId, j => j, StringComparer.OrdinalIgnoreCase);
        var hosts = (await db.Hosts.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(h => h.LastHeartbeatAtUtc)
            .ToList();
        var rows = hosts.Select(h => MapHost(h, customers, configsByHostId, policies, latestJobByHostId)).ToArray();

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            rows = rows.Where(h => string.Equals(h.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            rows = rows.Where(h =>
                string.Equals(h.HeartbeatStatus, status, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(h.OperationalStatusLabel, status, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(recoveryStatus))
        {
            rows = rows.Where(h => string.Equals(h.RecoveryStatusLabel, recoveryStatus, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        return View(new HostsPageViewModel
        {
            CustomerId = customerId,
            Status = status,
            RecoveryStatus = recoveryStatus,
            Hosts = rows
        });
    }

    [HttpGet("/admin/hosts/new")]
    public IActionResult NewHost([FromQuery] string? customerId)
    {
        return View("HostForm", new HostFormViewModel
        {
            Id = string.Empty,
            CustomerId = customerId ?? string.Empty,
            Hostname = string.Empty,
            OsVersion = "Windows Server 2016",
            IsEditMode = false
        });
    }

    [HttpPost("/admin/hosts/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateHost([FromForm] HostFormViewModel form, CancellationToken ct)
    {
        var error = ValidateHostForm(form, isEditMode: false);
        if (error is not null)
        {
            logger.LogWarning("Host creation validation failed for proposed host {HostId}: {Error}", form.Id, error);
            return View("HostForm", form with { ErrorMessage = error, IsEditMode = false });
        }

        var hostId = form.Id.Trim();
        var customerId = form.CustomerId.Trim();
        var customerExists = await db.Customers.AnyAsync(c => c.Id == customerId, ct);
        if (!customerExists)
        {
            logger.LogWarning("Host creation rejected because customer {CustomerId} was not found", customerId);
            return View("HostForm", form with { ErrorMessage = "Cliente informado nao existe.", IsEditMode = false });
        }

        var exists = await db.Hosts.AnyAsync(h => h.Id == hostId, ct);
        if (exists)
        {
            logger.LogWarning("Host creation rejected because host {HostId} already exists", hostId);
            return View("HostForm", form with { ErrorMessage = "Ja existe um host com este ID.", IsEditMode = false });
        }

        db.Hosts.Add(new ControlPlane.Api.Domain.Host
        {
            Id = hostId,
            CustomerId = customerId,
            Hostname = form.Hostname.Trim(),
            OsVersion = form.OsVersion.Trim(),
            FirstSeenAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Host {HostId} created for customer {CustomerId}", hostId, customerId);
        await RecordAuditAsync(
            category: "host",
            action: "create",
            entityType: "host",
            entityId: hostId,
            message: $"Host {hostId} criado para o cliente {customerId}.",
            customerId: customerId,
            hostId: hostId,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["hostname"] = form.Hostname.Trim(),
                ["osVersion"] = form.OsVersion.Trim()
            });

        return Redirect("/admin/hosts");
    }

    [HttpGet("/admin/hosts/{id}/edit")]
    public async Task<IActionResult> EditHost(string id, CancellationToken ct)
    {
        var model = await BuildHostFormViewModelAsync(id, ct);
        if (model is null)
        {
            return NotFound();
        }

        return View("HostForm", model);
    }

    [HttpPost("/admin/hosts/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditHostPost(string id, [FromForm] HostFormViewModel form, CancellationToken ct)
    {
        var error = ValidateHostForm(form, isEditMode: true);
        if (error is not null)
        {
            logger.LogWarning("Host edit validation failed for host {HostId}: {Error}", id, error);
            var invalidModel = await BuildHostFormViewModelAsync(id, ct, error);
            return invalidModel is null ? NotFound() : View("HostForm", invalidModel with
            {
                Hostname = form.Hostname,
                OsVersion = form.OsVersion
            });
        }

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (host is null)
        {
            return NotFound();
        }

        host.Hostname = form.Hostname.Trim();
        host.OsVersion = form.OsVersion.Trim();

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Host {HostId} updated", id);
        await RecordAuditAsync(
            category: "host",
            action: "edit",
            entityType: "host",
            entityId: id,
            message: $"Host {id} atualizado no painel.",
            customerId: host.CustomerId,
            hostId: id,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["hostname"] = host.Hostname,
                ["osVersion"] = host.OsVersion
            });

        return Redirect("/admin/hosts");
    }

    [HttpPost("/admin/hosts/{id}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteHost(string id, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == id, ct);
        if (host is null)
        {
            TempData["ErrorMessage"] = "Host nao encontrado.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/hosts");
        }

        var jobCount = await db.Jobs.AsNoTracking().CountAsync(j => j.HostId == id, ct);
        var alertCount = await db.Alerts.AsNoTracking().CountAsync(a => a.HostId == id, ct);
        var configurationCount = await db.AgentConfigurations.AsNoTracking().CountAsync(c => c.HostId == id, ct);
        var policyCount = await db.BackupPolicies.AsNoTracking().CountAsync(p => p.HostId == id, ct);
        if (jobCount > 0 || alertCount > 0 || configurationCount > 0 || policyCount > 0)
        {
            logger.LogWarning(
                "Host {HostId} deletion blocked due to dependencies. Jobs={JobCount}, Alerts={AlertCount}, Configurations={ConfigurationCount}, Policies={PolicyCount}",
                id,
                jobCount,
                alertCount,
                configurationCount,
                policyCount);

            TempData["ErrorMessage"] = $"Exclusao bloqueada. O host possui dependencias: jobs={jobCount}, alertas={alertCount}, configuracoes={configurationCount}, politicas={policyCount}.";
            return RedirectToLocalOrDefault(returnUrl, $"/admin/hosts/{Uri.EscapeDataString(id)}/edit");
        }

        db.Hosts.Remove(host);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Host {HostId} deleted", id);
        await RecordAuditAsync(
            category: "host",
            action: "delete",
            entityType: "host",
            entityId: id,
            message: $"Host {id} removido do painel.",
            customerId: host.CustomerId,
            hostId: id,
            ct);

        TempData["StatusMessage"] = "Host removido com sucesso.";
        return RedirectToLocalOrDefault(returnUrl, "/admin/hosts");
    }

    [HttpGet("/admin/policies")]
    public async Task<IActionResult> Policies([FromQuery] string? customerId, [FromQuery] string? scopeType, CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var hosts = await db.Hosts.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h, ct);
        var policies = await db.BackupPolicies.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        var rows = policies.Select(p => MapPolicy(p, customers, hosts)).ToArray();

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            rows = rows.Where(p => string.Equals(p.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(scopeType))
        {
            rows = rows.Where(p => string.Equals(p.ScopeType, scopeType, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        return View(new PoliciesPageViewModel
        {
            CustomerId = customerId,
            ScopeType = scopeType,
            Policies = rows
        });
    }

    [HttpGet("/admin/policies/new")]
    public IActionResult NewPolicy()
    {
        return View("PolicyForm", new PolicyFormViewModel
        {
            Id = string.Empty,
            CustomerId = string.Empty,
            HostId = string.Empty,
            Name = string.Empty,
            PolicyKind = "operational",
            OriginHostId = null,
            ScopeType = "customer",
            IncludePathsCsv = string.Empty,
            ExcludePathsCsv = string.Empty,
            AwsRegion = string.Empty,
            S3BucketName = string.Empty,
            S3KeyPrefix = string.Empty,
            ScheduleDaysCsv = "TUE,FRI",
            ScheduleDays = SplitCsvTokens("TUE,FRI"),
            StartTimeLocal = "22:00",
            MaxRuntimeMinutes = 720,
            CpuLimitPercent = 35,
            NetworkLimitMbit = 80,
            Enabled = true,
            IsEditMode = false
        });
    }

    [HttpPost("/admin/policies/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePolicy([FromForm] PolicyFormViewModel form, CancellationToken ct)
    {
        var normalizedForm = NormalizePolicyForm(form);
        var error = ValidatePolicyForm(normalizedForm, false);
        if (error is not null)
        {
            return View("PolicyForm", normalizedForm with { ErrorMessage = error, IsEditMode = false });
        }

        var exists = await db.BackupPolicies.AnyAsync(p => p.Id == normalizedForm.Id.Trim(), ct);
        if (exists)
        {
            return View("PolicyForm", normalizedForm with { ErrorMessage = "Ja existe uma politica com este ID.", IsEditMode = false });
        }

        var now = DateTimeOffset.UtcNow;
        var normalizedPolicyKind = NormalizePolicyKind(normalizedForm.PolicyKind, fallback: "operational");
        db.BackupPolicies.Add(new BackupPolicy
        {
            Id = normalizedForm.Id.Trim(),
            CustomerId = normalizedForm.CustomerId.Trim(),
            HostId = string.IsNullOrWhiteSpace(normalizedForm.HostId) ? null : normalizedForm.HostId.Trim(),
            Name = normalizedForm.Name.Trim(),
            PolicyKind = normalizedPolicyKind,
            OriginHostId = string.IsNullOrWhiteSpace(normalizedForm.OriginHostId) ? null : normalizedForm.OriginHostId.Trim(),
            ScopeType = normalizedForm.ScopeType.Trim(),
            IncludePathsCsv = normalizedForm.IncludePathsCsv.Trim(),
            ExcludePathsCsv = string.IsNullOrWhiteSpace(normalizedForm.ExcludePathsCsv) ? null : normalizedForm.ExcludePathsCsv.Trim(),
            AwsRegion = string.IsNullOrWhiteSpace(normalizedForm.AwsRegion) ? null : normalizedForm.AwsRegion.Trim(),
            S3BucketName = string.IsNullOrWhiteSpace(normalizedForm.S3BucketName) ? null : normalizedForm.S3BucketName.Trim(),
            S3KeyPrefix = string.IsNullOrWhiteSpace(normalizedForm.S3KeyPrefix) ? null : normalizedForm.S3KeyPrefix.Trim().Trim('/'),
            ScheduleDaysCsv = normalizedForm.ScheduleDaysCsv.Trim(),
            StartTimeLocal = normalizedForm.StartTimeLocal.Trim(),
            MaxRuntimeMinutes = normalizedForm.MaxRuntimeMinutes,
            CpuLimitPercent = normalizedForm.CpuLimitPercent,
            NetworkLimitMbit = normalizedForm.NetworkLimitMbit,
            Enabled = normalizedForm.Enabled,
            LastChangedAtUtc = now,
            CreatedAtUtc = now
        });

        db.PolicyChangeEvents.Add(BuildPolicyChangeEvent(
            policyId: normalizedForm.Id.Trim(),
            eventType: "policy_created",
            message: normalizedPolicyKind == "bootstrap"
                ? "Politica bootstrap criada manualmente."
                : "Politica operacional criada manualmente."));

        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(
            category: "policy",
            action: "create",
            entityType: "policy",
            entityId: normalizedForm.Id.Trim(),
            message: $"Politica {normalizedForm.Id.Trim()} criada no painel.",
            customerId: normalizedForm.CustomerId.Trim(),
            hostId: string.IsNullOrWhiteSpace(normalizedForm.HostId) ? null : normalizedForm.HostId.Trim(),
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["name"] = normalizedForm.Name.Trim(),
                ["scopeType"] = normalizedForm.ScopeType.Trim(),
                ["policyKind"] = normalizedPolicyKind
            });
        return Redirect("/admin/policies");
    }

    [HttpGet("/admin/policies/{id}/edit")]
    public async Task<IActionResult> EditPolicy(string id, CancellationToken ct)
    {
        var policy = await db.BackupPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        var events = (await db.PolicyChangeEvents.AsNoTracking()
            .Where(e => e.PolicyId == id)
            .ToListAsync(ct))
            .OrderByDescending(e => e.CreatedAtUtc)
            .Take(20)
            .ToArray();

        return View("PolicyForm", new PolicyFormViewModel
        {
            OriginalId = policy.Id,
            Id = policy.Id,
            CustomerId = policy.CustomerId,
            HostId = policy.HostId,
            Name = policy.Name,
            PolicyKind = NormalizePolicyKind(policy.PolicyKind, fallback: "operational"),
            OriginHostId = policy.OriginHostId,
            ScopeType = policy.ScopeType,
            IncludePathsCsv = policy.IncludePathsCsv,
            ExcludePathsCsv = policy.ExcludePathsCsv,
            AwsRegion = policy.AwsRegion,
            S3BucketName = policy.S3BucketName,
            S3KeyPrefix = policy.S3KeyPrefix,
            ScheduleDaysCsv = policy.ScheduleDaysCsv,
            ScheduleDays = SplitCsvTokens(policy.ScheduleDaysCsv),
            StartTimeLocal = policy.StartTimeLocal,
            MaxRuntimeMinutes = policy.MaxRuntimeMinutes,
            CpuLimitPercent = policy.CpuLimitPercent,
            NetworkLimitMbit = policy.NetworkLimitMbit,
            Enabled = policy.Enabled,
            IsEditMode = true,
            LastChangedAtUtc = policy.LastChangedAtUtc,
            RecentEvents = events.Select(e => new PolicyChangeEventViewModel
            {
                EventType = e.EventType,
                Message = e.Message,
                CreatedAtUtc = e.CreatedAtUtc
            }).ToArray()
        });
    }

    [HttpPost("/admin/policies/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPolicyPost(string id, [FromForm] PolicyFormViewModel form, CancellationToken ct)
    {
        var normalizedForm = NormalizePolicyForm(form) with { OriginalId = id, IsEditMode = true };
        var error = ValidatePolicyForm(normalizedForm, true);
        if (error is not null)
        {
            return View("PolicyForm", normalizedForm with { ErrorMessage = error, OriginalId = id, IsEditMode = true });
        }

        var policy = await db.BackupPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        var previousKind = NormalizePolicyKind(policy.PolicyKind, fallback: "operational");
        var previousEnabled = policy.Enabled;
        var normalizedPolicyKind = NormalizePolicyKind(normalizedForm.PolicyKind, fallback: previousKind);

        policy.CustomerId = normalizedForm.CustomerId.Trim();
        policy.HostId = string.IsNullOrWhiteSpace(normalizedForm.HostId) ? null : normalizedForm.HostId.Trim();
        policy.Name = normalizedForm.Name.Trim();
        policy.PolicyKind = normalizedPolicyKind;
        policy.OriginHostId = string.IsNullOrWhiteSpace(normalizedForm.OriginHostId) ? null : normalizedForm.OriginHostId.Trim();
        policy.ScopeType = normalizedForm.ScopeType.Trim();
        policy.IncludePathsCsv = normalizedForm.IncludePathsCsv.Trim();
        policy.ExcludePathsCsv = string.IsNullOrWhiteSpace(normalizedForm.ExcludePathsCsv) ? null : normalizedForm.ExcludePathsCsv.Trim();
        policy.AwsRegion = string.IsNullOrWhiteSpace(normalizedForm.AwsRegion) ? null : normalizedForm.AwsRegion.Trim();
        policy.S3BucketName = string.IsNullOrWhiteSpace(normalizedForm.S3BucketName) ? null : normalizedForm.S3BucketName.Trim();
        policy.S3KeyPrefix = string.IsNullOrWhiteSpace(normalizedForm.S3KeyPrefix) ? null : normalizedForm.S3KeyPrefix.Trim().Trim('/');
        policy.ScheduleDaysCsv = normalizedForm.ScheduleDaysCsv.Trim();
        policy.StartTimeLocal = normalizedForm.StartTimeLocal.Trim();
        policy.MaxRuntimeMinutes = normalizedForm.MaxRuntimeMinutes;
        policy.CpuLimitPercent = normalizedForm.CpuLimitPercent;
        policy.NetworkLimitMbit = normalizedForm.NetworkLimitMbit;
        policy.Enabled = normalizedForm.Enabled;
        policy.LastChangedAtUtc = DateTimeOffset.UtcNow;

        db.PolicyChangeEvents.Add(BuildPolicyChangeEvent(
            policyId: policy.Id,
            eventType: previousKind != normalizedPolicyKind ? "policy_kind_changed" : "policy_updated",
            message: BuildPolicyUpdateMessage(previousKind, normalizedPolicyKind, previousEnabled, policy.Enabled)));

        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(
            category: "policy",
            action: "edit",
            entityType: "policy",
            entityId: policy.Id,
            message: $"Politica {policy.Id} atualizada no painel.",
            customerId: policy.CustomerId,
            hostId: policy.HostId,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["name"] = policy.Name,
                ["scopeType"] = policy.ScopeType,
                ["policyKind"] = policy.PolicyKind
            });
        return Redirect("/admin/policies");
    }

    [HttpGet("/admin/configurations")]
    public async Task<IActionResult> Configurations([FromQuery] string? customerId, [FromQuery] string? serviceStatus, [FromQuery] string? recoveryStatus, CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var hosts = await db.Hosts.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h, ct);
        var policies = await db.BackupPolicies.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p, ct);
        var configs = await db.AgentConfigurations.AsNoTracking().OrderBy(c => c.CustomerId).ThenBy(c => c.HostId).ToListAsync(ct);
        var rows = configs.Select(c => MapAgentConfiguration(c, customers, hosts, policies)).ToArray();

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            rows = rows.Where(c => string.Equals(c.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(serviceStatus))
        {
            rows = rows.Where(c => string.Equals(c.ServiceStatus, serviceStatus, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(recoveryStatus))
        {
            rows = rows.Where(c => string.Equals(c.RecoveryStatusLabel, recoveryStatus, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        return View(new AgentConfigurationsPageViewModel
        {
            CustomerId = customerId,
            ServiceStatus = serviceStatus,
            RecoveryStatus = recoveryStatus,
            Configurations = rows
        });
    }

    [HttpGet("/admin/configurations/{id}")]
    public async Task<IActionResult> ConfigurationDetail(string id, CancellationToken ct)
    {
        var model = await BuildConfigurationDetailViewModelAsync(id, ct);
        if (model is null)
        {
            return NotFound();
        }

        return View("ConfigurationDetail", model);
    }

    [HttpGet("/admin/aws/prefix-options")]
    public async Task<IActionResult> AwsPrefixOptions([FromQuery] string? expectedAccountId, [FromQuery] string? bucketName, CancellationToken ct)
    {
        var result = await awsDiscoveryService.ListPrefixesForBucketAsync(expectedAccountId, bucketName, ct);
        return Json(new
        {
            success = result.Success,
            bucketRegion = result.BucketRegion,
            prefixes = result.Prefixes,
            message = result.Message
        });
    }

    [HttpPost("/admin/configurations/{id}/policy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateConfigurationPolicy(string id, [FromForm] string? policyId, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var configuration = await db.AgentConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (configuration is null)
        {
            TempData["ErrorMessage"] = "Configuracao nao encontrada.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/configurations");
        }

        var trimmedPolicyId = string.IsNullOrWhiteSpace(policyId) ? null : policyId.Trim();
        if (trimmedPolicyId is not null)
        {
            var host = await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == configuration.HostId, ct);
            if (host is null)
            {
                TempData["ErrorMessage"] = "Host da configuracao nao encontrado.";
                return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
            }

            var readiness = hostOperationalStatusService.Evaluate(host, configuration);
            if (!readiness.IsReadyForPolicyAssignment)
            {
                TempData["ErrorMessage"] = readiness.Message;
                return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
            }

            var policy = await db.BackupPolicies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == trimmedPolicyId, ct);
            if (policy is null)
            {
                TempData["ErrorMessage"] = "Politica selecionada nao existe.";
                return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
            }

            if (!policy.Enabled)
            {
                logger.LogWarning("Policy assignment rejected because policy {PolicyId} is disabled for configuration {ConfigurationId}", trimmedPolicyId, id);
                TempData["ErrorMessage"] = "A politica selecionada esta desativada.";
                return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
            }

            var isValidScope =
                string.Equals(policy.CustomerId, configuration.CustomerId, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(policy.ScopeType, "customer", StringComparison.OrdinalIgnoreCase) ||
                 (string.Equals(policy.ScopeType, "host", StringComparison.OrdinalIgnoreCase) &&
                  string.Equals(policy.HostId, configuration.HostId, StringComparison.OrdinalIgnoreCase)));

            if (!isValidScope)
            {
                logger.LogWarning(
                    "Policy assignment rejected due to invalid scope. Policy {PolicyId}, Configuration {ConfigurationId}, Customer {CustomerId}, Host {HostId}",
                    trimmedPolicyId,
                    id,
                    configuration.CustomerId,
                    configuration.HostId);
                TempData["ErrorMessage"] = "A politica selecionada nao pertence ao mesmo cliente/host desta configuracao.";
                return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
            }
        }

        configuration.PolicyId = trimmedPolicyId;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Configuration {ConfigurationId} policy updated to {PolicyId}", id, trimmedPolicyId ?? "<none>");
        await RecordAuditAsync(
            category: "configuration",
            action: trimmedPolicyId is null ? "unbind_policy" : "bind_policy",
            entityType: "agent_configuration",
            entityId: id,
            message: trimmedPolicyId is null
                ? $"Politica desvinculada da configuracao {id}."
                : $"Politica {trimmedPolicyId} vinculada a configuracao {id}.",
            customerId: configuration.CustomerId,
            hostId: configuration.HostId,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["policyId"] = trimmedPolicyId
            });

        TempData["StatusMessage"] = trimmedPolicyId is null
            ? "Politica desvinculada com sucesso."
            : "Politica vinculada com sucesso.";
        return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
    }

    [HttpPost("/admin/configurations/{id}/bootstrap-policy")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBootstrapPolicy(string id, [FromForm] BootstrapPolicyDraftViewModel form, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var configuration = await db.AgentConfigurations.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (configuration is null)
        {
            TempData["ErrorMessage"] = "Configuracao nao encontrada.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/configurations");
        }

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == configuration.HostId, ct);
        if (host is null)
        {
            TempData["ErrorMessage"] = "Host da configuracao nao encontrado.";
            return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
        }

        var bootstrapForm = NormalizeBootstrapPolicyDraft(form, configuration, host);
        if (!bootstrapForm.HasBootstrapPaths)
        {
            TempData["ErrorMessage"] = "Este host ainda nao reportou paths iniciais do Agent.";
            return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
        }

        var error = ValidatePolicyForm(new PolicyFormViewModel
        {
            Id = bootstrapForm.SuggestedPolicyId,
            CustomerId = bootstrapForm.CustomerId,
            HostId = bootstrapForm.HostId,
            Name = bootstrapForm.Name,
            PolicyKind = "bootstrap",
            OriginHostId = bootstrapForm.HostId,
            ScopeType = bootstrapForm.ScopeType,
            IncludePathsCsv = bootstrapForm.IncludePathsCsv,
            ExcludePathsCsv = bootstrapForm.ExcludePathsCsv,
            AwsRegion = bootstrapForm.AwsRegion,
            S3BucketName = bootstrapForm.S3BucketName,
            S3KeyPrefix = bootstrapForm.S3KeyPrefix,
            ScheduleDaysCsv = bootstrapForm.ScheduleDaysCsv,
            StartTimeLocal = bootstrapForm.StartTimeLocal,
            MaxRuntimeMinutes = bootstrapForm.MaxRuntimeMinutes,
            CpuLimitPercent = bootstrapForm.CpuLimitPercent,
            NetworkLimitMbit = bootstrapForm.NetworkLimitMbit,
            Enabled = bootstrapForm.Enabled,
            IsEditMode = false
        }, isEditMode: false);
        if (error is not null)
        {
            TempData["ErrorMessage"] = error;
            return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
        }

        var policyId = bootstrapForm.SuggestedPolicyId;
        var policy = await db.BackupPolicies.FirstOrDefaultAsync(p => p.Id == policyId, ct);
        var policyEventType = "bootstrap_policy_updated";
        var policyEventMessage = "Politica bootstrap atualizada a partir do host.";
        if (policy is null)
        {
            policy = new BackupPolicy
            {
                Id = policyId,
                CustomerId = bootstrapForm.CustomerId,
                HostId = bootstrapForm.HostId,
                Name = bootstrapForm.Name,
                PolicyKind = "bootstrap",
                OriginHostId = bootstrapForm.HostId,
                ScopeType = bootstrapForm.ScopeType,
                IncludePathsCsv = bootstrapForm.IncludePathsCsv,
                ExcludePathsCsv = bootstrapForm.ExcludePathsCsv,
                AwsRegion = bootstrapForm.AwsRegion,
                S3BucketName = bootstrapForm.S3BucketName,
                S3KeyPrefix = bootstrapForm.S3KeyPrefix,
                ScheduleDaysCsv = bootstrapForm.ScheduleDaysCsv,
                StartTimeLocal = bootstrapForm.StartTimeLocal,
                MaxRuntimeMinutes = bootstrapForm.MaxRuntimeMinutes,
                CpuLimitPercent = bootstrapForm.CpuLimitPercent,
                NetworkLimitMbit = bootstrapForm.NetworkLimitMbit,
                Enabled = bootstrapForm.Enabled,
                LastChangedAtUtc = DateTimeOffset.UtcNow,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.BackupPolicies.Add(policy);
            policyEventType = "bootstrap_policy_created";
            policyEventMessage = "Politica bootstrap criada a partir do bootstrap do host.";
        }
        else
        {
            policy.CustomerId = bootstrapForm.CustomerId;
            policy.HostId = bootstrapForm.HostId;
            policy.Name = bootstrapForm.Name;
            policy.PolicyKind = "bootstrap";
            policy.OriginHostId = bootstrapForm.HostId;
            policy.ScopeType = bootstrapForm.ScopeType;
            policy.IncludePathsCsv = bootstrapForm.IncludePathsCsv;
            policy.ExcludePathsCsv = bootstrapForm.ExcludePathsCsv;
            policy.AwsRegion = bootstrapForm.AwsRegion;
            policy.S3BucketName = bootstrapForm.S3BucketName;
            policy.S3KeyPrefix = bootstrapForm.S3KeyPrefix;
            policy.ScheduleDaysCsv = bootstrapForm.ScheduleDaysCsv;
            policy.StartTimeLocal = bootstrapForm.StartTimeLocal;
            policy.MaxRuntimeMinutes = bootstrapForm.MaxRuntimeMinutes;
            policy.CpuLimitPercent = bootstrapForm.CpuLimitPercent;
            policy.NetworkLimitMbit = bootstrapForm.NetworkLimitMbit;
            policy.Enabled = bootstrapForm.Enabled;
            policy.LastChangedAtUtc = DateTimeOffset.UtcNow;
        }

        db.PolicyChangeEvents.Add(BuildPolicyChangeEvent(policyId, policyEventType, policyEventMessage));

        var readiness = hostOperationalStatusService.Evaluate(host, configuration);
        if (readiness.IsReadyForPolicyAssignment)
        {
            configuration.PolicyId = policyId;
            TempData["StatusMessage"] = "Politica inicial criada a partir do bootstrap e vinculada ao host.";
            db.PolicyChangeEvents.Add(BuildPolicyChangeEvent(policyId, "bootstrap_policy_auto_assigned", "Politica bootstrap vinculada automaticamente ao host apos readiness."));
        }
        else
        {
            TempData["StatusMessage"] = "Politica inicial criada a partir do bootstrap. O vinculo automatico ficou pendente porque o host ainda nao esta pronto.";
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Bootstrap policy {PolicyId} processed for configuration {ConfigurationId}. AutoAssigned={AutoAssigned}",
            policyId,
            id,
            readiness.IsReadyForPolicyAssignment);
        await RecordAuditAsync(
            category: "configuration",
            action: "bootstrap_policy",
            entityType: "agent_configuration",
            entityId: id,
            message: readiness.IsReadyForPolicyAssignment
                ? $"Politica bootstrap {policyId} criada/atualizada e vinculada automaticamente."
                : $"Politica bootstrap {policyId} criada/atualizada, aguardando vinculacao manual.",
            customerId: configuration.CustomerId,
            hostId: configuration.HostId,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["policyId"] = policyId,
                ["autoAssigned"] = readiness.IsReadyForPolicyAssignment ? "true" : "false"
            });

        return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
    }

    [HttpPost("/admin/configurations/{id}/run-now")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QueueRunNow(string id, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var configuration = await db.AgentConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (configuration is null)
        {
            TempData["ErrorMessage"] = "Configuracao nao encontrada.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/configurations");
        }

        var pendingRequest = (await db.AgentRunRequests.AsNoTracking()
            .Where(r => r.CustomerId == configuration.CustomerId &&
                        r.HostId == configuration.HostId &&
                        (r.State == "QUEUED" || r.State == "CLAIMED"))
            .ToListAsync(ct))
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefault();
        if (pendingRequest is not null)
        {
            TempData["ErrorMessage"] = "Ja existe uma execucao manual pendente para este host.";
            return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
        }

        var runRequest = new AgentRunRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            CustomerId = configuration.CustomerId,
            HostId = configuration.HostId,
            TriggerType = "manual_controlplane",
            State = "QUEUED",
            RequestedBy = HttpContext.GetCurrentPanelUser()?.Email ?? "controlplane-user",
            RequestedAtUtc = DateTimeOffset.UtcNow
        };
        db.AgentRunRequests.Add(runRequest);

        await db.SaveChangesAsync(ct);
        await RecordAuditAsync(
            category: "operation",
            action: "run_now",
            entityType: "agent_configuration",
            entityId: id,
            message: $"Execucao manual enfileirada para o host {configuration.HostId}.",
            customerId: configuration.CustomerId,
            hostId: configuration.HostId,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["runRequestId"] = runRequest.Id
            });
        TempData["StatusMessage"] = "Execucao manual enfileirada. O Agent vai consumir a requisicao no proximo ciclo.";
        return RedirectToLocalOrDefault(returnUrl, $"/admin/configurations/{Uri.EscapeDataString(id)}");
    }

    [HttpGet("/admin/jobs")]
    public async Task<IActionResult> Jobs([FromQuery] string? customerId, [FromQuery] string? hostId, [FromQuery] string? state, CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var hosts = await db.Hosts.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h, ct);
        var jobs = (await db.Jobs.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(j => j.StartedAtUtc)
            .ToList();
        var rows = jobs.Select(j => MapJob(j, customers, hosts)).ToArray();

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            rows = rows.Where(j => string.Equals(j.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(hostId))
        {
            rows = rows.Where(j => string.Equals(j.HostId, hostId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(state))
        {
            rows = rows.Where(j => string.Equals(j.State, state, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        return View(new JobsPageViewModel
        {
            CustomerId = customerId,
            HostId = hostId,
            State = state,
            Jobs = rows
        });
    }

    [HttpGet("/admin/jobs/{id}")]
    public async Task<IActionResult> JobDetail(string id, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null)
        {
            return NotFound();
        }

        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var hosts = await db.Hosts.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h, ct);
        var alerts = (await db.Alerts.AsNoTracking().Where(a => a.JobId == id).ToListAsync(ct))
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToList();
        var artifacts = (await db.Artifacts.AsNoTracking().Where(a => a.JobId == id).ToListAsync(ct))
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToList();

        return View("JobDetail", new JobDetailViewModel
        {
            Job = MapJob(job, customers, hosts),
            Artifacts = artifacts.Select(a => new ArtifactRowViewModel
            {
                Id = a.Id,
                Type = a.Type,
                Location = a.Location,
                CreatedAtUtc = a.CreatedAtUtc
            }).ToArray(),
            Alerts = alerts.Select(a => MapAlert(a, customers, hosts)).ToArray()
        });
    }

    [HttpGet("/admin/alerts")]
    public async Task<IActionResult> Alerts([FromQuery] string? customerId, [FromQuery] string? severity, [FromQuery] string? status, [FromQuery] string? recoveryStatus, CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var hosts = await db.Hosts.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h, ct);
        var configsByHostId = (await db.AgentConfigurations.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.HostId)
            .Select(g => g.OrderByDescending(x => x.LastConfigSyncAtUtc ?? x.CreatedAtUtc).FirstOrDefault()!)
            .ToDictionary(c => c.HostId, c => c, StringComparer.OrdinalIgnoreCase);
        var latestJobsByHostId = (await db.Jobs.AsNoTracking().ToListAsync(ct))
            .GroupBy(j => j.HostId)
            .Select(g => g.OrderByDescending(x => x.StartedAtUtc).First())
            .ToDictionary(j => j.HostId, j => j, StringComparer.OrdinalIgnoreCase);
        var alerts = (await db.Alerts.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(a => a.LastObservedAtUtc)
            .ToList();
        var rows = alerts.Select(a => MapAlert(a, customers, hosts, configsByHostId, latestJobsByHostId)).ToArray();

        if (!string.IsNullOrWhiteSpace(customerId))
        {
            rows = rows.Where(a => string.Equals(a.CustomerId, customerId, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(severity))
        {
            rows = rows.Where(a => string.Equals(a.Severity, severity, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(a => a.ResolvedAtUtc is null && a.AcknowledgedAtUtc is null).ToArray();
        }
        else if (string.Equals(status, "acknowledged", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(a => a.ResolvedAtUtc is null && a.AcknowledgedAtUtc is not null).ToArray();
        }
        else if (string.Equals(status, "resolved", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(a => a.ResolvedAtUtc is not null).ToArray();
        }
        if (!string.IsNullOrWhiteSpace(recoveryStatus))
        {
            rows = rows.Where(a => string.Equals(a.RecoveryStatusLabel, recoveryStatus, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        return View(new AlertsPageViewModel
        {
            CustomerId = customerId,
            Severity = severity,
            Status = status,
            RecoveryStatus = recoveryStatus,
            Analytics = BuildAlertAnalyticsSummary(
                rows.Select(r => alerts.First(a => a.Id == r.Id)).ToArray(),
                customers,
                hosts),
            Alerts = rows
        });
    }

    [HttpPost("/admin/alerts/{id}/ack")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AcknowledgeAlert(string id, [FromForm] string? returnUrl, CancellationToken ct)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (alert is null)
        {
            TempData["ErrorMessage"] = "Alerta nao encontrado.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/alerts");
        }

        if (alert.AcknowledgedAtUtc is not null)
        {
            TempData["StatusMessage"] = "Alerta ja estava reconhecido.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/alerts");
        }

        if (alert.ResolvedAtUtc is not null)
        {
            TempData["StatusMessage"] = "Alerta ja foi resolvido automaticamente.";
            return RedirectToLocalOrDefault(returnUrl, "/admin/alerts");
        }

        alert.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Alert {AlertId} acknowledged for customer {CustomerId}", id, alert.CustomerId);
        await RecordAuditAsync(
            category: "alert",
            action: "acknowledge",
            entityType: "alert",
            entityId: id,
            message: $"Alerta {id} reconhecido no painel.",
            customerId: alert.CustomerId,
            hostId: alert.HostId,
            ct,
            metadata: new Dictionary<string, string?>
            {
                ["severity"] = alert.Severity,
                ["type"] = alert.Type
            });

        TempData["StatusMessage"] = "Alerta reconhecido com sucesso.";
        return RedirectToLocalOrDefault(returnUrl, "/admin/alerts");
    }

    private static string? ValidateCustomerForm(CustomerFormViewModel form, bool isEditMode)
    {
        if (!isEditMode && string.IsNullOrWhiteSpace(form.Id))
        {
            return "ID do cliente e obrigatorio.";
        }
        if (!isEditMode && !IsAgentCompatibleIdentifier(form.Id))
        {
            return "ID do cliente invalido. Use apenas letras sem acento, numeros, ponto, hifen ou underscore (sem espacos), comecando por letra ou numero.";
        }
        if (string.IsNullOrWhiteSpace(form.Name))
        {
            return "Nome do cliente e obrigatorio.";
        }
        if (string.IsNullOrWhiteSpace(form.AwsAccountId))
        {
            return "Conta AWS e obrigatoria.";
        }

        var accountId = form.AwsAccountId.Trim();
        if (accountId.Length is < 12 or > 32)
        {
            return "Conta AWS deve ter entre 12 e 32 caracteres.";
        }

        return null;
    }

    private static string? ValidateHostForm(HostFormViewModel form, bool isEditMode)
    {
        if (!isEditMode && string.IsNullOrWhiteSpace(form.Id))
        {
            return "ID do host e obrigatorio.";
        }
        if (!isEditMode && !IsAgentCompatibleIdentifier(form.Id))
        {
            return "ID do host invalido. Use apenas letras sem acento, numeros, ponto, hifen ou underscore (sem espacos), comecando por letra ou numero.";
        }
        if (string.IsNullOrWhiteSpace(form.CustomerId))
        {
            return "CustomerId e obrigatorio.";
        }
        if (string.IsNullOrWhiteSpace(form.Hostname))
        {
            return "Hostname e obrigatorio.";
        }
        if (string.IsNullOrWhiteSpace(form.OsVersion))
        {
            return "Versao do sistema operacional e obrigatoria.";
        }
        if (form.Hostname.Trim().Length > 255)
        {
            return "Hostname deve ter no maximo 255 caracteres.";
        }
        if (form.OsVersion.Trim().Length > 64)
        {
            return "Versao do sistema operacional deve ter no maximo 64 caracteres.";
        }

        return null;
    }

    private static PolicyFormViewModel NormalizePolicyForm(PolicyFormViewModel form)
    {
        var scheduleDaysCsv = NormalizeScheduleDays(form.ScheduleDays, form.ScheduleDaysCsv, fallback: "TUE,FRI");
        return form with
        {
            Id = string.IsNullOrWhiteSpace(form.Id) ? string.Empty : form.Id.Trim(),
            CustomerId = string.IsNullOrWhiteSpace(form.CustomerId) ? string.Empty : form.CustomerId.Trim(),
            HostId = NormalizeOptionalValue(form.HostId),
            Name = string.IsNullOrWhiteSpace(form.Name) ? string.Empty : form.Name.Trim(),
            PolicyKind = NormalizePolicyKind(form.PolicyKind, fallback: "operational"),
            OriginHostId = NormalizeOptionalValue(form.OriginHostId),
            ScopeType = string.IsNullOrWhiteSpace(form.ScopeType) ? "customer" : form.ScopeType.Trim(),
            IncludePathsCsv = NormalizePathCsv(form.IncludePathsCsv) ?? string.Empty,
            ExcludePathsCsv = NormalizePathCsv(form.ExcludePathsCsv),
            AwsRegion = NormalizeOptionalValue(form.AwsRegion),
            S3BucketName = NormalizeOptionalValue(form.S3BucketName),
            S3KeyPrefix = NormalizePrefix(form.S3KeyPrefix),
            ScheduleDaysCsv = scheduleDaysCsv,
            ScheduleDays = SplitCsvTokens(scheduleDaysCsv),
            StartTimeLocal = NormalizeScheduleTime(form.StartTimeLocal, fallback: "22:00"),
            MaxRuntimeMinutes = ClampPositive(form.MaxRuntimeMinutes, fallback: 720),
            CpuLimitPercent = ClampRange(form.CpuLimitPercent, min: 1, max: 100, fallback: 35),
            NetworkLimitMbit = ClampRange(form.NetworkLimitMbit, min: 1, max: 100_000, fallback: 80)
        };
    }

    private static string? ValidatePolicyForm(PolicyFormViewModel form, bool isEditMode)
    {
        if (!isEditMode && string.IsNullOrWhiteSpace(form.Id))
        {
            return "ID da politica e obrigatorio.";
        }
        if (string.IsNullOrWhiteSpace(form.CustomerId))
        {
            return "CustomerId e obrigatorio.";
        }
        if (string.IsNullOrWhiteSpace(form.Name))
        {
            return "Nome da politica e obrigatorio.";
        }
        if (NormalizePolicyKind(form.PolicyKind, fallback: string.Empty) is not ("bootstrap" or "operational"))
        {
            return "PolicyKind deve ser 'bootstrap' ou 'operational'.";
        }
        if (string.IsNullOrWhiteSpace(form.IncludePathsCsv))
        {
            return "Ao menos um path de inclusao e obrigatorio.";
        }
        if (string.IsNullOrWhiteSpace(form.ScheduleDaysCsv))
        {
            return "Dias da agenda sao obrigatorios.";
        }
        if (form.ScopeType != "customer" && form.ScopeType != "host")
        {
            return "ScopeType deve ser 'customer' ou 'host'.";
        }
        if (form.ScopeType == "host" && string.IsNullOrWhiteSpace(form.HostId))
        {
            return "HostId e obrigatorio para politicas por host.";
        }
        if (!string.IsNullOrWhiteSpace(form.AwsRegion) || !string.IsNullOrWhiteSpace(form.S3BucketName) || !string.IsNullOrWhiteSpace(form.S3KeyPrefix))
        {
            if (string.IsNullOrWhiteSpace(form.AwsRegion))
            {
                return "AwsRegion e obrigatoria quando houver destino S3.";
            }
            if (string.IsNullOrWhiteSpace(form.S3BucketName))
            {
                return "Bucket S3 e obrigatorio quando houver destino S3.";
            }
            if (string.IsNullOrWhiteSpace(form.S3KeyPrefix))
            {
                return "Pasta/prefixo S3 e obrigatorio quando houver destino S3.";
            }
        }
        if (form.MaxRuntimeMinutes <= 0 || form.CpuLimitPercent <= 0 || form.NetworkLimitMbit <= 0)
        {
            return "Runtime, CPU e rede devem ser maiores que zero.";
        }

        return null;
    }

    private HostRowViewModel MapHost(
        ControlPlane.Api.Domain.Host host,
        IReadOnlyDictionary<string, Customer> customers,
        IReadOnlyDictionary<string, AgentConfiguration> configsByHostId,
        IReadOnlyDictionary<string, BackupPolicy> policiesById,
        IReadOnlyDictionary<string, Job>? latestJobByHostId = null)
    {
        customers.TryGetValue(host.CustomerId, out var customer);
        configsByHostId.TryGetValue(host.Id, out var config);
        BackupPolicy? policy = null;
        if (config?.PolicyId is not null)
        {
            policiesById.TryGetValue(config.PolicyId, out policy);
        }

        Job? latestJob = null;
        latestJobByHostId?.TryGetValue(host.Id, out latestJob);

        var operational = hostOperationalStatusService.Evaluate(host, config);
        var bootstrap = DescribeBootstrapState(host.CustomerId, host.Id, host.BootstrapIncludePathsCsv, config?.PolicyId, policiesById);
        var health = BuildOperationalHealthViewModel(host, config, latestJob: null, latestRunRequest: null, operational);

        return new HostRowViewModel
        {
            Id = host.Id,
            CustomerId = host.CustomerId,
            CustomerName = customer?.Name,
            ConfigurationId = config?.Id,
            Hostname = host.Hostname,
            OsVersion = host.OsVersion,
            LastHeartbeatAtUtc = host.LastHeartbeatAtUtc,
            HeartbeatStatus = GetHeartbeatStatus(host.LastHeartbeatAtUtc),
            AgentVersion = config?.AgentVersion,
            ServiceStatus = config?.ServiceStatus,
            AssignedPolicyName = policy?.Name,
            PrecheckTlsOk = config?.PrecheckTlsOk,
            PrecheckDiskOk = config?.PrecheckDiskOk,
            PrecheckCredentialOk = config?.PrecheckCredentialOk,
            OperationalStatusLabel = operational.Label,
            OperationalStatusCssClass = operational.CssClass,
            OperationalStatusMessage = operational.Message,
            IsReadyForPolicyAssignment = operational.IsReadyForPolicyAssignment,
            BootstrapStatusLabel = bootstrap.Label,
            BootstrapStatusCssClass = bootstrap.CssClass,
            BootstrapStatusMessage = bootstrap.Message,
            RecoveryStatusLabel = health.RiskLevelLabel,
            RecoveryStatusCssClass = health.RiskLevelCssClass,
            RecoveryStatusMessage = health.Summary,
            LastJobState = latestJob?.State,
            LastJobAtUtc = latestJob?.StartedAtUtc
        };
    }

    private static JobRowViewModel MapJob(Job job, IReadOnlyDictionary<string, Customer> customers, IReadOnlyDictionary<string, ControlPlane.Api.Domain.Host> hosts)
    {
        customers.TryGetValue(job.CustomerId, out var customer);
        hosts.TryGetValue(job.HostId, out var host);
        return new JobRowViewModel
        {
            Id = job.Id,
            CustomerId = job.CustomerId,
            CustomerName = customer?.Name,
            HostId = job.HostId,
            Hostname = host?.Hostname,
            State = job.State,
            StartedAtUtc = job.StartedAtUtc,
            FinishedAtUtc = job.FinishedAtUtc,
            PlannedBytes = job.PlannedBytes,
            UploadedBytes = job.UploadedBytes,
            PlannedItems = job.PlannedItems,
            UploadedItems = job.UploadedItems,
            FailureCode = job.FailureCode,
            FailureMessage = job.FailureMessage
        };
    }

    private AlertRowViewModel MapAlert(
        Alert alert,
        IReadOnlyDictionary<string, Customer> customers,
        IReadOnlyDictionary<string, ControlPlane.Api.Domain.Host> hosts,
        IReadOnlyDictionary<string, AgentConfiguration>? configsByHostId = null,
        IReadOnlyDictionary<string, Job>? latestJobsByHostId = null)
    {
        customers.TryGetValue(alert.CustomerId, out var customer);
        ControlPlane.Api.Domain.Host? host = null;
        AgentConfiguration? configuration = null;
        Job? latestJob = null;
        HostOperationalHealthViewModel? health = null;
        string? suggestedActionText = null;
        string? suggestedActionUrl = null;

        if (alert.HostId is not null)
        {
            hosts.TryGetValue(alert.HostId, out host);
            if (host is not null)
            {
                configsByHostId?.TryGetValue(host.Id, out configuration);
                latestJobsByHostId?.TryGetValue(host.Id, out latestJob);
                health = BuildOperationalHealthViewModel(host, configuration, latestJob, latestRunRequest: null);
                (suggestedActionText, suggestedActionUrl) = BuildAlertSuggestedAction(alert, host, configuration, health);
            }
        }

        return new AlertRowViewModel
        {
            Id = alert.Id,
            CustomerId = alert.CustomerId,
            CustomerName = customer?.Name,
            HostId = alert.HostId,
            Hostname = host?.Hostname,
            JobId = alert.JobId,
            Type = alert.Type,
            Severity = alert.Severity,
            Message = alert.Message,
            RecoveryStatusLabel = health?.RiskLevelLabel,
            RecoveryStatusCssClass = health?.RiskLevelCssClass,
            RecoveryStatusMessage = health?.Summary,
            SuggestedActionText = suggestedActionText,
            SuggestedActionUrl = suggestedActionUrl,
            Source = alert.Source,
            RootCauseKey = alert.RootCauseKey,
            CreatedAtUtc = alert.CreatedAtUtc,
            LastObservedAtUtc = alert.LastObservedAtUtc,
            AcknowledgedAtUtc = alert.AcknowledgedAtUtc,
            ResolvedAtUtc = alert.ResolvedAtUtc
        };
    }

    private AlertAnalyticsSummaryViewModel BuildAlertAnalyticsSummary(
        IEnumerable<Alert> alerts,
        IReadOnlyDictionary<string, Customer> customers,
        IReadOnlyDictionary<string, ControlPlane.Api.Domain.Host> hosts)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var orderedAlerts = alerts
            .OrderByDescending(a => a.LastObservedAtUtc)
            .ToArray();
        var rootCauseGroups = orderedAlerts
            .GroupBy(a => string.IsNullOrWhiteSpace(a.RootCauseKey) ? $"legacy:{a.Id}" : a.RootCauseKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var resolvedDurations = orderedAlerts
            .Where(a => a.ResolvedAtUtc is not null && a.ResolvedAtUtc >= a.CreatedAtUtc)
            .Select(a => a.ResolvedAtUtc!.Value - a.CreatedAtUtc)
            .ToArray();
        var unresolvedDurations = orderedAlerts
            .Where(a => a.ResolvedAtUtc is null)
            .Select(a => nowUtc - a.CreatedAtUtc)
            .ToArray();

        return new AlertAnalyticsSummaryViewModel
        {
            OpenAlertCount = orderedAlerts.Count(a => a.ResolvedAtUtc is null),
            AcknowledgedAlertCount = orderedAlerts.Count(a => a.ResolvedAtUtc is null && a.AcknowledgedAtUtc is not null),
            ResolvedAlertCount = orderedAlerts.Count(a => a.ResolvedAtUtc is not null),
            DistinctRootCauseCount = rootCauseGroups.Length,
            ReincidentRootCauseCount = rootCauseGroups.Count(g => g.Count() > 1),
            AverageTimeToResolveLabel = resolvedDurations.Length == 0 ? "-" : FormatDurationLabel(TimeSpan.FromTicks((long)resolvedDurations.Average(d => d.Ticks))),
            LongestOpenDurationLabel = unresolvedDurations.Length == 0 ? "-" : FormatDurationLabel(unresolvedDurations.Max()),
            Timeline = BuildAlertTimelineItems(orderedAlerts, customers, hosts),
            TopRootCauses = BuildAlertCauseAnalytics(rootCauseGroups, hosts)
        };
    }

    private IReadOnlyList<AlertTimelineItemViewModel> BuildAlertTimelineItems(
        IReadOnlyList<Alert> orderedAlerts,
        IReadOnlyDictionary<string, Customer> customers,
        IReadOnlyDictionary<string, ControlPlane.Api.Domain.Host> hosts)
    {
        return orderedAlerts
            .Take(8)
            .Select(alert =>
            {
                customers.TryGetValue(alert.CustomerId, out var customer);
                ControlPlane.Api.Domain.Host? host = null;
                if (!string.IsNullOrWhiteSpace(alert.HostId))
                {
                    hosts.TryGetValue(alert.HostId, out host);
                }

                return new AlertTimelineItemViewModel
                {
                    Title = DescribeAlertType(alert.Type),
                    Severity = alert.Severity,
                    StatusLabel = alert.ResolvedAtUtc is not null ? "Resolvido" : alert.AcknowledgedAtUtc is not null ? "Reconhecido" : "Aberto",
                    StatusCssClass = alert.ResolvedAtUtc is not null ? "ready" : alert.AcknowledgedAtUtc is not null ? "succeeded" : MapAlertSeverityCssClass(alert.Severity),
                    Message = alert.Message,
                    CustomerId = alert.CustomerId,
                    CustomerName = customer?.Name,
                    HostId = alert.HostId,
                    Hostname = host?.Hostname,
                    LinkText = BuildAlertTimelineLinkText(alert),
                    LinkUrl = BuildAlertTimelineLinkUrl(alert),
                    ObservedAtUtc = alert.LastObservedAtUtc
                };
            })
            .ToArray();
    }

    private IReadOnlyList<AlertCauseAnalyticsViewModel> BuildAlertCauseAnalytics(
        IEnumerable<IGrouping<string, Alert>> rootCauseGroups,
        IReadOnlyDictionary<string, ControlPlane.Api.Domain.Host> hosts)
    {
        return rootCauseGroups
            .Select(group =>
            {
                var alerts = group
                    .OrderByDescending(a => a.LastObservedAtUtc)
                    .ToArray();
                var latest = alerts[0];
                ControlPlane.Api.Domain.Host? latestHost = null;
                if (!string.IsNullOrWhiteSpace(latest.HostId))
                {
                    hosts.TryGetValue(latest.HostId, out latestHost);
                }

                var resolvedDurations = alerts
                    .Where(a => a.ResolvedAtUtc is not null && a.ResolvedAtUtc >= a.CreatedAtUtc)
                    .Select(a => a.ResolvedAtUtc!.Value - a.CreatedAtUtc)
                    .ToArray();

                return new AlertCauseAnalyticsViewModel
                {
                    RootCauseKey = group.Key,
                    Title = DescribeAlertType(latest.Type),
                    Severity = PickHighestSeverity(alerts.Select(a => a.Severity)),
                    OccurrenceCount = alerts.Length,
                    OpenCount = alerts.Count(a => a.ResolvedAtUtc is null),
                    ResolvedCount = alerts.Count(a => a.ResolvedAtUtc is not null),
                    AverageTimeToResolveLabel = resolvedDurations.Length == 0
                        ? "-"
                        : FormatDurationLabel(TimeSpan.FromTicks((long)resolvedDurations.Average(d => d.Ticks))),
                    LatestHostId = latest.HostId,
                    LatestHostname = latestHost?.Hostname,
                    LatestObservedAtUtc = latest.LastObservedAtUtc,
                    LinkText = BuildAlertTimelineLinkText(latest),
                    LinkUrl = BuildAlertTimelineLinkUrl(latest)
                };
            })
            .OrderByDescending(x => x.OpenCount)
            .ThenByDescending(x => x.OccurrenceCount)
            .ThenByDescending(x => x.LatestObservedAtUtc)
            .Take(6)
            .ToArray();
    }

    private static string DescribeAlertType(string? alertType)
    {
        if (string.IsNullOrWhiteSpace(alertType))
        {
            return "Alerta operacional";
        }

        return alertType.Trim().ToUpperInvariant() switch
        {
            "JOB_FAILED" => "Job com falha",
            "HOST_OFFLINE" => "Host offline",
            "HOST_NO_HEARTBEAT" => "Host sem heartbeat",
            "HOST_CONFIG_MISSING" => "Configuracao ausente",
            "HOST_CONFIG_SYNC_MISSING" => "Sync de configuracao ausente",
            "HOST_CONFIG_SYNC_STALE" => "Sync de configuracao desatualizada",
            "HOST_SERVICE_NOT_RUNNING" => "Servico do Agent indisponivel",
            "HOST_TLS_FAILED" => "Falha de TLS",
            "HOST_STAGING_FAILED" => "Falha de staging local",
            "HOST_AWS_CREDENTIAL_FAILED" => "Falha de credencial AWS",
            "HOST_POLICY_MISSING" => "Politica ausente",
            _ => alertType.Replace('_', ' ')
        };
    }

    private static string PickHighestSeverity(IEnumerable<string?> severities)
    {
        var normalized = severities
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!.Trim().ToUpperInvariant())
            .ToArray();

        if (normalized.Contains("CRITICAL", StringComparer.OrdinalIgnoreCase))
        {
            return "CRITICAL";
        }

        if (normalized.Contains("WARNING", StringComparer.OrdinalIgnoreCase))
        {
            return "WARNING";
        }

        return normalized.FirstOrDefault() ?? "INFO";
    }

    private static string MapAlertSeverityCssClass(string? severity)
    {
        return string.IsNullOrWhiteSpace(severity) ? "not-ready" : severity.Trim().ToLowerInvariant();
    }

    private static string? BuildAlertTimelineLinkText(Alert alert)
    {
        if (!string.IsNullOrWhiteSpace(alert.JobId))
        {
            return "Abrir job";
        }

        if (!string.IsNullOrWhiteSpace(alert.HostId))
        {
            return "Abrir host";
        }

        return !string.IsNullOrWhiteSpace(alert.CustomerId) ? "Abrir cliente" : null;
    }

    private static string? BuildAlertTimelineLinkUrl(Alert alert)
    {
        if (!string.IsNullOrWhiteSpace(alert.JobId))
        {
            return $"/admin/jobs/{Uri.EscapeDataString(alert.JobId)}";
        }

        if (!string.IsNullOrWhiteSpace(alert.HostId))
        {
            return $"/admin/hosts/{Uri.EscapeDataString(alert.HostId)}/edit";
        }

        return !string.IsNullOrWhiteSpace(alert.CustomerId)
            ? $"/admin/customers/{Uri.EscapeDataString(alert.CustomerId)}"
            : null;
    }

    private static string FormatDurationLabel(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m";
        }

        return $"{Math.Max(0, (int)duration.TotalSeconds)}s";
    }

    private static AuditEventRowViewModel MapAuditEvent(AuditEvent auditEvent)
    {
        return new AuditEventRowViewModel
        {
            Category = auditEvent.Category,
            Action = auditEvent.Action,
            Outcome = auditEvent.Outcome,
            EntityType = auditEvent.EntityType,
            EntityId = auditEvent.EntityId,
            ActorDisplayName = auditEvent.ActorDisplayName,
            ActorEmail = auditEvent.ActorEmail,
            Message = auditEvent.Message,
            CreatedAtUtc = auditEvent.CreatedAtUtc
        };
    }

    private static DashboardHealthSummary BuildDashboardHealthSummary(
        DateTimeOffset nowUtc,
        IReadOnlyList<ControlPlane.Api.Domain.Host> hosts,
        IEnumerable<AgentConfiguration> configurations,
        int pendingManualRunCount)
    {
        var latestConfigurations = configurations.ToArray();
        return new DashboardHealthSummary
        {
            OfflineHostCount = hosts.Count(h => h.LastHeartbeatAtUtc is null || h.LastHeartbeatAtUtc < nowUtc.AddMinutes(-20)),
            CredentialFailureHostCount = latestConfigurations.Count(c => !c.PrecheckCredentialOk),
            StaleConfigurationCount = latestConfigurations.Count(c => c.LastConfigSyncAtUtc is null || c.LastConfigSyncAtUtc < nowUtc.AddMinutes(-30)),
            PendingManualRunCount = pendingManualRunCount
        };
    }

    private static IReadOnlyList<OperationalRiskItemViewModel> BuildOperationalRisks(
        DateTimeOffset nowUtc,
        IReadOnlyList<ControlPlane.Api.Domain.Host> allHosts,
        IReadOnlyDictionary<string, AgentConfiguration> configsByHostId,
        IReadOnlyDictionary<string, Job> latestJobsByHostId,
        IReadOnlyDictionary<string, Customer> customersById)
    {
        var items = new List<OperationalRiskItemViewModel>();

        foreach (var host in allHosts.OrderByDescending(h => h.LastHeartbeatAtUtc))
        {
            customersById.TryGetValue(host.CustomerId, out var customer);
            var hostLabel = string.IsNullOrWhiteSpace(host.Hostname) ? host.Id : host.Hostname;
            var customerLabel = customer?.Name ?? host.CustomerId;

            if (host.LastHeartbeatAtUtc is null || host.LastHeartbeatAtUtc < nowUtc.AddMinutes(-20))
            {
                items.Add(new OperationalRiskItemViewModel
                {
                    Title = $"{hostLabel} sem heartbeat",
                    Severity = "high",
                    Message = $"Host do cliente {customerLabel} sem heartbeat recente. Ultimo sinal: {(host.LastHeartbeatAtUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "nunca")}.",
                    LinkText = "Abrir host",
                    LinkUrl = $"/admin/hosts/{Uri.EscapeDataString(host.Id)}/edit"
                });
            }

            if (configsByHostId.TryGetValue(host.Id, out var configuration))
            {
                if (!configuration.PrecheckCredentialOk)
                {
                    items.Add(new OperationalRiskItemViewModel
                    {
                        Title = $"{hostLabel} com AWS pendente",
                        Severity = "warning",
                        Message = $"Host do cliente {customerLabel} reportou falha de credencial AWS. Ultima sync: {(configuration.LastConfigSyncAtUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "-")}.",
                        LinkText = "Abrir configuracao",
                        LinkUrl = $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}"
                    });
                }

                if (configuration.LastConfigSyncAtUtc is null || configuration.LastConfigSyncAtUtc < nowUtc.AddMinutes(-30))
                {
                    items.Add(new OperationalRiskItemViewModel
                    {
                        Title = $"{hostLabel} com sync desatualizada",
                        Severity = "warning",
                        Message = $"A configuracao do host {hostLabel} nao sincroniza com o painel desde {(configuration.LastConfigSyncAtUtc?.ToLocalTime().ToString("dd/MM/yyyy HH:mm") ?? "o onboarding")}.",
                        LinkText = "Abrir configuracao",
                        LinkUrl = $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}"
                    });
                }
            }

            if (latestJobsByHostId.TryGetValue(host.Id, out var latestJob) &&
                string.Equals(latestJob.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                items.Add(new OperationalRiskItemViewModel
                {
                    Title = $"{hostLabel} com ultimo job falho",
                    Severity = "high",
                    Message = $"Ultimo job do host {hostLabel} falhou em {latestJob.StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}.",
                    LinkText = "Abrir job",
                    LinkUrl = $"/admin/jobs/{Uri.EscapeDataString(latestJob.Id)}"
                });
            }
        }

        return items
            .OrderByDescending(item => item.Severity == "high")
            .ThenBy(item => item.Title)
            .Take(12)
            .ToArray();
    }

    private static PolicyListItemViewModel MapPolicy(BackupPolicy policy, IReadOnlyDictionary<string, Customer> customers, IReadOnlyDictionary<string, ControlPlane.Api.Domain.Host> hosts)
    {
        customers.TryGetValue(policy.CustomerId, out var customer);
        ControlPlane.Api.Domain.Host? host = null;
        if (!string.IsNullOrWhiteSpace(policy.HostId))
        {
            hosts.TryGetValue(policy.HostId, out host);
        }

        return new PolicyListItemViewModel
        {
            Id = policy.Id,
            CustomerId = policy.CustomerId,
            CustomerName = customer?.Name,
            HostId = policy.HostId,
            Hostname = host?.Hostname,
            Name = policy.Name,
            PolicyKind = NormalizePolicyKind(policy.PolicyKind, fallback: "operational"),
            OriginHostId = policy.OriginHostId,
            ScopeType = policy.ScopeType,
            IncludePathsCsv = policy.IncludePathsCsv,
            ExcludePathsCsv = policy.ExcludePathsCsv,
            AwsRegion = policy.AwsRegion,
            S3BucketName = policy.S3BucketName,
            S3KeyPrefix = policy.S3KeyPrefix,
            ScheduleDaysCsv = policy.ScheduleDaysCsv,
            StartTimeLocal = policy.StartTimeLocal,
            MaxRuntimeMinutes = policy.MaxRuntimeMinutes,
            CpuLimitPercent = policy.CpuLimitPercent,
            NetworkLimitMbit = policy.NetworkLimitMbit,
            Enabled = policy.Enabled,
            LastChangedAtUtc = policy.LastChangedAtUtc,
            CreatedAtUtc = policy.CreatedAtUtc
        };
    }

    private AgentConfigurationListItemViewModel MapAgentConfiguration(
        AgentConfiguration configuration,
        IReadOnlyDictionary<string, Customer> customers,
        IReadOnlyDictionary<string, ControlPlane.Api.Domain.Host> hosts,
        IReadOnlyDictionary<string, BackupPolicy> policies)
    {
        customers.TryGetValue(configuration.CustomerId, out var customer);
        hosts.TryGetValue(configuration.HostId, out var host);
        BackupPolicy? policy = null;
        if (!string.IsNullOrWhiteSpace(configuration.PolicyId))
        {
            policies.TryGetValue(configuration.PolicyId, out policy);
        }

        HostOperationalStatus? operational = null;
        if (host is not null)
        {
            operational = hostOperationalStatusService.Evaluate(host, configuration);
        }
        var bootstrap = DescribeBootstrapState(configuration.CustomerId, configuration.HostId, host?.BootstrapIncludePathsCsv, configuration.PolicyId, policies);
        var health = BuildOperationalHealthViewModel(host, configuration, latestJob: null, latestRunRequest: null, operational);

        return new AgentConfigurationListItemViewModel
        {
            Id = configuration.Id,
            CustomerId = configuration.CustomerId,
            CustomerName = customer?.Name,
            HostId = configuration.HostId,
            Hostname = host?.Hostname,
            PolicyId = configuration.PolicyId,
            PolicyName = policy?.Name,
            EffectivePolicyId = configuration.EffectivePolicyId,
            EffectivePolicyName = configuration.EffectivePolicyName,
            EffectivePolicyKind = configuration.EffectivePolicyKind,
            EffectivePolicySource = configuration.EffectivePolicySource,
            EffectivePolicyLastChangedAtUtc = configuration.EffectivePolicyLastChangedAtUtc,
            AgentVersion = configuration.AgentVersion,
            ServiceStatus = configuration.ServiceStatus,
            TlsMode = configuration.TlsMode,
            PrecheckTlsOk = configuration.PrecheckTlsOk,
            PrecheckDiskOk = configuration.PrecheckDiskOk,
            PrecheckCredentialOk = configuration.PrecheckCredentialOk,
            StagingPath = configuration.StagingPath,
            CredentialTargetName = configuration.CredentialTargetName,
            UploadMode = configuration.UploadMode,
            LastConfigSyncAtUtc = configuration.LastConfigSyncAtUtc,
            LastPrecheckAtUtc = configuration.LastPrecheckAtUtc,
            LastPrecheckMessage = configuration.LastPrecheckMessage,
            OperationalStatusLabel = operational?.Label ?? "Nao validado",
            OperationalStatusCssClass = operational?.CssClass ?? "not-ready",
            OperationalStatusMessage = operational?.Message ?? "Host ainda nao localizado no cadastro operacional.",
            IsReadyForPolicyAssignment = operational?.IsReadyForPolicyAssignment ?? false,
            BootstrapIncludePathsCsv = host?.BootstrapIncludePathsCsv,
            BootstrapExcludePathsCsv = host?.BootstrapExcludePathsCsv,
            BootstrapStatusLabel = bootstrap.Label,
            BootstrapStatusCssClass = bootstrap.CssClass,
            BootstrapStatusMessage = bootstrap.Message,
            BootstrapPolicyExists = bootstrap.PolicyExists,
            IsBootstrapPolicyAssigned = bootstrap.IsAssigned,
            BootstrapPolicyId = bootstrap.PolicyId,
            RecoveryStatusLabel = health.RiskLevelLabel,
            RecoveryStatusCssClass = health.RiskLevelCssClass,
            RecoveryStatusMessage = health.Summary
        };
    }

    private string GetHeartbeatStatus(DateTimeOffset? lastHeartbeatAtUtc) => hostOperationalStatusService.GetHeartbeatStatus(lastHeartbeatAtUtc);

    private async Task<HostFormViewModel?> BuildHostFormViewModelAsync(string id, CancellationToken ct, string? errorMessage = null)
    {
        var host = await db.Hosts.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id, ct);
        if (host is null)
        {
            return null;
        }

        var latestConfiguration = (await db.AgentConfigurations.AsNoTracking()
            .Where(c => c.HostId == id)
            .ToListAsync(ct))
            .OrderByDescending(c => c.LastConfigSyncAtUtc ?? c.CreatedAtUtc)
            .FirstOrDefault();

        BackupPolicy? policy = null;
        if (!string.IsNullOrWhiteSpace(latestConfiguration?.PolicyId))
        {
            policy = await db.BackupPolicies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == latestConfiguration.PolicyId, ct);
        }

        var operational = hostOperationalStatusService.Evaluate(host, latestConfiguration);
        var jobs = (await db.Jobs.AsNoTracking()
            .Where(j => j.HostId == id)
            .ToListAsync(ct))
            .OrderByDescending(j => j.StartedAtUtc)
            .Take(20)
            .ToArray();
        var latestJob = jobs.FirstOrDefault();
        var latestRunRequest = (await db.AgentRunRequests.AsNoTracking()
            .Where(r => r.CustomerId == host.CustomerId && r.HostId == host.Id)
            .ToListAsync(ct))
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefault();
        var alertHistory = (await db.Alerts.AsNoTracking()
            .Where(a => a.CustomerId == host.CustomerId && a.HostId == host.Id)
            .ToListAsync(ct))
            .OrderByDescending(a => a.LastObservedAtUtc)
            .ToList();
        var hostMap = new Dictionary<string, ControlPlane.Api.Domain.Host>(StringComparer.OrdinalIgnoreCase)
        {
            [host.Id] = host
        };
        var customerMap = await db.Customers.AsNoTracking()
            .Where(c => c.Id == host.CustomerId)
            .ToDictionaryAsync(c => c.Id, c => c, ct);

        AwsIntegrationViewModel? awsIntegration = null;
        BootstrapPolicyDraftViewModel? bootstrapPolicyDraft = null;
        if (latestConfiguration is not null)
        {
            var policyMap = policy is null
                ? new Dictionary<string, BackupPolicy>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, BackupPolicy>(StringComparer.OrdinalIgnoreCase) { [policy.Id] = policy };
            var mappedConfiguration = MapAgentConfiguration(latestConfiguration, customerMap, hostMap, policyMap);
            awsIntegration = BuildAwsIntegrationViewModelFromAgent(
                customerMap.TryGetValue(host.CustomerId, out var customerForAws) ? customerForAws.AwsAccountId : null,
                policy?.S3BucketName,
                latestConfiguration);
            awsIntegration.CurrentPrefix = policy?.S3KeyPrefix;
            bootstrapPolicyDraft = BuildBootstrapPolicyDraft(latestConfiguration, host, mappedConfiguration, policy, awsIntegration);
        }

        return new HostFormViewModel
        {
            OriginalId = host.Id,
            Id = host.Id,
            CustomerId = host.CustomerId,
            Hostname = host.Hostname,
            OsVersion = host.OsVersion,
            IsEditMode = true,
            FirstSeenAtUtc = host.FirstSeenAtUtc,
            LastHeartbeatAtUtc = host.LastHeartbeatAtUtc,
            ConfigurationId = latestConfiguration?.Id,
            AgentVersion = latestConfiguration?.AgentVersion,
            ServiceStatus = latestConfiguration?.ServiceStatus,
            AssignedPolicyName = policy?.Name,
            OperationalStatusLabel = operational.Label,
            OperationalStatusCssClass = operational.CssClass,
            OperationalStatusMessage = operational.Message,
            BootstrapIncludePathsCsv = host.BootstrapIncludePathsCsv,
            BootstrapExcludePathsCsv = host.BootstrapExcludePathsCsv,
            OperationalHealth = BuildOperationalHealthViewModel(host, latestConfiguration, latestJob, latestRunRequest, operational),
            AlertAnalytics = BuildAlertAnalyticsSummary(alertHistory, customerMap, hostMap),
            AwsIntegration = awsIntegration,
            BootstrapPolicyDraft = bootstrapPolicyDraft,
            LatestRunRequest = BuildLatestRunViewModel(latestRunRequest, latestJob),
            RecentJobs = jobs.Select(j => MapJob(j, customerMap, hostMap)).ToArray(),
            ErrorMessage = errorMessage
        };
    }

    private async Task<AgentConfigurationDetailViewModel?> BuildConfigurationDetailViewModelAsync(string id, CancellationToken ct)
    {
        var configEntity = await db.AgentConfigurations.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (configEntity is null)
        {
            return null;
        }

        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var hosts = await db.Hosts.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h, ct);
        var policies = await db.BackupPolicies.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p, ct);
        var jobs = (await db.Jobs.AsNoTracking().Where(j => j.HostId == configEntity.HostId).ToListAsync(ct))
            .OrderByDescending(j => j.StartedAtUtc)
            .Take(20)
            .ToArray();
        var latestRunRequest = (await db.AgentRunRequests.AsNoTracking()
            .Where(r => r.CustomerId == configEntity.CustomerId && r.HostId == configEntity.HostId)
            .ToListAsync(ct))
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefault();

        hosts.TryGetValue(configEntity.HostId, out var host);
        var mappedConfiguration = MapAgentConfiguration(configEntity, customers, hosts, policies);
        var boundPolicy = ResolveBoundPolicy(configEntity, policies);
        var latestJob = jobs.FirstOrDefault();
        var awsIntegration = BuildAwsIntegrationViewModelFromAgent(
            customers.TryGetValue(configEntity.CustomerId, out var customer) ? customer.AwsAccountId : null,
            boundPolicy?.S3BucketName,
            configEntity);
        awsIntegration.CurrentPrefix = boundPolicy?.S3KeyPrefix;

        return new AgentConfigurationDetailViewModel
        {
            Configuration = mappedConfiguration,
            OperationalHealth = BuildOperationalHealthViewModel(
                host,
                configEntity,
                latestJob,
                latestRunRequest,
                host is null ? null : hostOperationalStatusService.Evaluate(host, configEntity)),
            AwsIntegration = awsIntegration,
            LatestRunRequest = BuildLatestRunViewModel(latestRunRequest, latestJob),
            RecentJobs = jobs.Select(j => MapJob(j, customers, hosts)).ToArray(),
            PolicyOptions = await BuildPolicyOptionsAsync(configEntity.CustomerId, configEntity.HostId, ct),
            BootstrapPolicyDraft = BuildBootstrapPolicyDraft(configEntity, host, mappedConfiguration, boundPolicy, awsIntegration)
        };
    }

    private HostOperationalHealthViewModel BuildOperationalHealthViewModel(
        ControlPlane.Api.Domain.Host? host,
        AgentConfiguration? configuration,
        Job? latestJob,
        AgentRunRequest? latestRunRequest,
        HostOperationalStatus? operational = null)
    {
        if (host is null)
        {
            return new HostOperationalHealthViewModel
            {
                RiskLevelLabel = "Critico",
                RiskLevelCssClass = "critical",
                Summary = "A configuracao chegou ao painel, mas o cadastro operacional do host nao foi reconciliado.",
                Signals =
                [
                    new OperationalHealthSignalViewModel
                    {
                        Name = "Cadastro operacional",
                        StatusLabel = "Pendente",
                        StatusCssClass = "critical",
                        Detail = "Cadastre ou reconcilie o host para liberar diagnostico completo, politica e suporte operacional."
                    }
                ],
                RecoverySteps =
                [
                    new OperationalRecoveryStepViewModel
                    {
                        Severity = "critical",
                        Title = "Reconciliar o host no cadastro operacional",
                        Message = "A configuracao foi recebida, mas o painel nao encontrou o host correspondente. Revise o onboarding, IDs e eventuais reinstalacoes parciais do Agent."
                    }
                ]
            };
        }

        operational ??= hostOperationalStatusService.Evaluate(host, configuration);
        var nowUtc = DateTimeOffset.UtcNow;
        var heartbeatStatus = hostOperationalStatusService.GetHeartbeatStatus(host.LastHeartbeatAtUtc);
        var hasConfiguration = configuration is not null;
        var heartbeatMissing = host.LastHeartbeatAtUtc is null;
        var heartbeatOffline = string.Equals(heartbeatStatus, "Offline", StringComparison.OrdinalIgnoreCase);
        var heartbeatDelayed = string.Equals(heartbeatStatus, "Atrasado", StringComparison.OrdinalIgnoreCase);
        var syncMissing = configuration?.LastConfigSyncAtUtc is null;
        var syncStale = configuration?.LastConfigSyncAtUtc is not null && configuration.LastConfigSyncAtUtc < nowUtc.AddMinutes(-30);
        var serviceIssue = hasConfiguration && !IsRunningLike(configuration!.ServiceStatus);
        var tlsIssue = hasConfiguration && !configuration!.PrecheckTlsOk;
        var diskIssue = hasConfiguration && !configuration!.PrecheckDiskOk;
        var credentialIssue = hasConfiguration && !configuration!.PrecheckCredentialOk;
        var latestJobFailed = latestJob is not null && string.Equals(latestJob.State, "FAILED", StringComparison.OrdinalIgnoreCase);
        var latestJobSucceeded = latestJob is not null && string.Equals(latestJob.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase);
        var manualRunPending = latestRunRequest is not null &&
            (string.Equals(latestRunRequest.State, "QUEUED", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(latestRunRequest.State, "CLAIMED", StringComparison.OrdinalIgnoreCase));
        var noPolicyLinked = hasConfiguration &&
            string.IsNullOrWhiteSpace(configuration!.PolicyId) &&
            string.IsNullOrWhiteSpace(configuration.EffectivePolicyId);

        var signals = new List<OperationalHealthSignalViewModel>();
        AddSignal(
            signals,
            "Heartbeat",
            heartbeatStatus,
            MapHeartbeatCssClass(heartbeatStatus),
            host.LastHeartbeatAtUtc is null
                ? "O Agent ainda nao publicou heartbeat para este host."
                : $"Ultimo heartbeat em {host.LastHeartbeatAtUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss}.");

        AddSignal(
            signals,
            "Servico",
            hasConfiguration ? (configuration!.ServiceStatus ?? "Desconhecido") : "Sem sync",
            hasConfiguration ? MapServiceStatusCssClass(configuration!.ServiceStatus) : "not-ready",
            hasConfiguration
                ? $"Versao reportada: {configuration!.AgentVersion}. Upload: {configuration.UploadMode}."
                : "O painel ainda nao recebeu sincronizacao de configuracao deste host.");

        AddSignal(
            signals,
            "Sincronizacao",
            !hasConfiguration ? "Pendente" : syncMissing ? "Sem sync" : syncStale ? "Desatualizada" : "Atual",
            !hasConfiguration || syncMissing ? "not-ready" : syncStale ? "warning" : "ready",
            !hasConfiguration || syncMissing
                ? "A configuracao do Agent ainda nao foi recebida pelo painel."
                : $"Ultima sync em {configuration!.LastConfigSyncAtUtc!.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss}.");

        AddSignal(
            signals,
            "TLS",
            !hasConfiguration ? "Pendente" : configuration!.PrecheckTlsOk ? "OK" : "Falhou",
            !hasConfiguration ? "not-ready" : configuration!.PrecheckTlsOk ? "ready" : "critical",
            !hasConfiguration
                ? "Sem precheck TLS porque a configuracao ainda nao sincronizou."
                : $"{configuration!.TlsMode} | {(configuration.PrecheckTlsOk ? "Canal seguro validado." : SafePrecheckMessage(configuration, "Validacao TLS falhou."))}");

        AddSignal(
            signals,
            "Disco staging",
            !hasConfiguration ? "Pendente" : configuration!.PrecheckDiskOk ? "OK" : "Falhou",
            !hasConfiguration ? "not-ready" : configuration!.PrecheckDiskOk ? "ready" : "critical",
            !hasConfiguration
                ? "Sem validacao de staging porque a configuracao ainda nao sincronizou."
                : $"{configuration!.StagingPath} | {(configuration.PrecheckDiskOk ? "Espaco e acesso local validados." : SafePrecheckMessage(configuration, "Espaco ou permissao local insuficiente."))}");

        AddSignal(
            signals,
            "Credencial AWS",
            !hasConfiguration ? "Pendente" : configuration!.PrecheckCredentialOk ? "OK" : "Falhou",
            !hasConfiguration ? "not-ready" : configuration!.PrecheckCredentialOk ? "ready" : "warning",
            !hasConfiguration
                ? "Sem validacao da credencial AWS porque a configuracao ainda nao sincronizou."
                : $"{configuration!.CredentialTargetName} | {(configuration.PrecheckCredentialOk ? "Credencial local pronta para upload." : SafePrecheckMessage(configuration, "Credencial AWS local invalida ou ausente."))}");

        AddSignal(
            signals,
            "Politica",
            !hasConfiguration ? "Sem sync" : noPolicyLinked ? "Pendente" : "Vinculada",
            !hasConfiguration ? "not-ready" : noPolicyLinked ? "warning" : "ready",
            !hasConfiguration
                ? "O host ainda nao sincronizou configuracao suficiente para avaliacao de politica."
                : noPolicyLinked
                    ? "Existe readiness operacional, mas ainda nao ha politica operacional vinculada."
                    : $"Politica efetiva: {configuration!.EffectivePolicyName ?? configuration.EffectivePolicyId ?? configuration.PolicyId ?? "-"}.");

        AddSignal(
            signals,
            "Ultimo job",
            latestJob is null ? "Sem historico" : latestJob.State,
            latestJob is null ? "not-ready" : latestJobFailed ? "critical" : latestJobSucceeded ? "ready" : "warning",
            latestJob is null
                ? "Ainda nao existe job conhecido para este host."
                : latestJobFailed
                    ? $"Falhou em {latestJob.StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}. {latestJob.FailureMessage ?? latestJob.FailureCode ?? "Sem detalhe adicional."}"
                    : $"Ultima execucao em {latestJob.StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss} com status {latestJob.State}.");

        AddSignal(
            signals,
            "Execucao manual",
            latestRunRequest is null ? "Nenhuma" : latestRunRequest.State,
            latestRunRequest is null ? "not-ready" : manualRunPending ? "warning" : string.Equals(latestRunRequest.State, "FAILED", StringComparison.OrdinalIgnoreCase) ? "critical" : "ready",
            latestRunRequest is null
                ? "Nao ha solicitacao manual recente em fila."
                : $"Solicitada em {latestRunRequest.RequestedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}. {(string.IsNullOrWhiteSpace(latestRunRequest.FailureMessage) ? "Sem falha registrada." : latestRunRequest.FailureMessage)}");

        var recoverySteps = new List<OperationalRecoveryStepViewModel>();
        AddRecoveryStep(
            recoverySteps,
            heartbeatMissing || heartbeatOffline,
            "critical",
            "Restabelecer heartbeat do Agent",
            heartbeatMissing
                ? "O painel ainda nao recebeu heartbeat deste host. Valide se o servico do Agent foi instalado, iniciado e se consegue sair para o ControlPlane."
                : "O host ficou offline para o painel. Valide servico Windows, firewall, proxy corporativo e resolucao DNS antes de alterar politica.",
            configuration?.Id is not null ? "Abrir configuracao" : "Revisar host",
            configuration?.Id is not null ? $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}" : $"/admin/hosts/{Uri.EscapeDataString(host.Id)}/edit");

        AddRecoveryStep(
            recoverySteps,
            !hasConfiguration || syncMissing || syncStale,
            heartbeatOffline || heartbeatMissing ? "warning" : "critical",
            "Confirmar sincronizacao com o ControlPlane",
            !hasConfiguration || syncMissing
                ? "O Agent ainda nao publicou configuracao completa. Aguarde o primeiro ciclo ou valide a URL do painel, token de enrollment e comunicacao HTTPS."
                : "A ultima sync ficou desatualizada. Isso indica que o host nao esta conseguindo renovar estado operacional com o painel.",
            configuration?.Id is not null ? "Ver detalhes" : null,
            configuration?.Id is not null ? $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}" : null);

        AddRecoveryStep(
            recoverySteps,
            serviceIssue,
            "critical",
            "Corrigir o servico Windows do Agent",
            "O servico nao esta em execucao normal. Reinicie o servico, revise logs locais e valide se houve update parcial ou instalacao corrompida.",
            configuration?.Id is not null ? "Abrir configuracao" : null,
            configuration?.Id is not null ? $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}" : null);

        AddRecoveryStep(
            recoverySteps,
            tlsIssue,
            "critical",
            "Validar TLS minimo e cadeia de confianca",
            "O precheck TLS falhou. Em ambiente corporativo, revise TLS 1.2, certificados, inspeccao SSL, proxy e bloqueios intermediarios antes de reexecutar backup.",
            configuration?.Id is not null ? "Ver diagnostico" : null,
            configuration?.Id is not null ? $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}" : null);

        AddRecoveryStep(
            recoverySteps,
            diskIssue,
            "critical",
            "Liberar staging local e permissoes",
            "O precheck de disco falhou. Revise espaco livre, ACL do caminho de staging e eventuais bloqueios de antivirus ou ransomware protection no host.",
            configuration?.Id is not null ? "Ver staging" : null,
            configuration?.Id is not null ? $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}" : null);

        AddRecoveryStep(
            recoverySteps,
            credentialIssue,
            "warning",
            "Regravar a credencial AWS local",
            "O host segue comunicando com o painel, mas o upload continuara bloqueado ate que a credencial AWS local seja corrigida e validada novamente.",
            configuration?.Id is not null ? "Abrir configuracao" : null,
            configuration?.Id is not null ? $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}" : null);

        AddRecoveryStep(
            recoverySteps,
            noPolicyLinked && operational.IsReadyForPolicyAssignment,
            "warning",
            "Vincular politica operacional",
            "O host ja esta apto para operacao, mas ainda nao possui politica vinculada. Sem esse passo nao existe agenda efetiva de backup.",
            configuration?.Id is not null ? "Gerenciar politica" : null,
            configuration?.Id is not null ? $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}" : null);

        AddRecoveryStep(
            recoverySteps,
            latestJobFailed && latestJob is not null,
            "critical",
            "Analisar o ultimo job falho",
            latestJob is null
                ? "Existe indicio de falha no ultimo job, mas os detalhes nao ficaram disponiveis na carga atual. Atualize a pagina e valide o historico do host."
                : $"O ultimo job conhecido falhou em {latestJob.StartedAtUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss}. Revise o erro antes de liberar novos hosts para este mesmo padrao operacional.",
            latestJob is not null ? "Abrir job" : null,
            latestJob is not null ? $"/admin/jobs/{Uri.EscapeDataString(latestJob.Id)}" : null);

        AddRecoveryStep(
            recoverySteps,
            manualRunPending && latestRunRequest is not null,
            "warning",
            "Acompanhar execucao manual pendente",
            "Existe uma solicitacao manual aguardando consumo pelo Agent. Se permanecer em fila por muito tempo, revalide heartbeat, sync e comunicacao do host.",
            configuration?.Id is not null ? "Abrir configuracao" : null,
            configuration?.Id is not null ? $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}" : null);

        if (recoverySteps.Count == 0)
        {
            recoverySteps.Add(new OperationalRecoveryStepViewModel
            {
                Severity = "ready",
                Title = "Operacao estavel",
                Message = "O host esta sincronizado, com prechecks aprovados e sem acao corretiva imediata pendente."
            });
        }

        var riskLevelLabel = "Estavel";
        var riskLevelCssClass = "ready";
        var summary = "Host sincronizado, com sinais operacionais consistentes e sem bloqueio atual.";

        if (heartbeatMissing || heartbeatOffline || !hasConfiguration || serviceIssue || tlsIssue || diskIssue || latestJobFailed)
        {
            riskLevelLabel = "Critico";
            riskLevelCssClass = "critical";
            summary = "Existe bloqueio operacional que pode impedir backup, sincronizacao ou confiabilidade do host.";
        }
        else if (credentialIssue || syncStale || heartbeatDelayed || manualRunPending || (noPolicyLinked && operational.IsReadyForPolicyAssignment))
        {
            riskLevelLabel = "Atencao";
            riskLevelCssClass = "warning";
            summary = "O host esta parcialmente operacional, mas ainda requer acao de suporte para reduzir risco de falha ou lacuna de cobertura.";
        }

        return new HostOperationalHealthViewModel
        {
            RiskLevelLabel = riskLevelLabel,
            RiskLevelCssClass = riskLevelCssClass,
            Summary = summary,
            Signals = signals,
            RecoverySteps = recoverySteps
        };
    }

    private async Task<IReadOnlyList<PolicyOptionViewModel>> BuildPolicyOptionsAsync(string customerId, string hostId, CancellationToken ct)
    {
        var policies = await db.BackupPolicies.AsNoTracking()
            .Where(p => p.CustomerId == customerId && p.Enabled)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return policies
            .Where(p => string.Equals(p.ScopeType, "customer", StringComparison.OrdinalIgnoreCase) ||
                        (string.Equals(p.ScopeType, "host", StringComparison.OrdinalIgnoreCase) &&
                         string.Equals(p.HostId, hostId, StringComparison.OrdinalIgnoreCase)))
            .Select(p => new PolicyOptionViewModel
            {
                Id = p.Id,
                Label = $"{p.Name} [{(string.Equals(p.ScopeType, "host", StringComparison.OrdinalIgnoreCase) ? "Host" : "Cliente")}]"
            })
            .ToArray();
    }

    private static BootstrapPolicyDraftViewModel BuildBootstrapPolicyDraft(
        AgentConfiguration configuration,
        ControlPlane.Api.Domain.Host? host,
        AgentConfigurationListItemViewModel mappedConfiguration,
        BackupPolicy? boundPolicy,
        AwsIntegrationViewModel awsIntegration)
    {
        var bootstrapIncludePathsCsv = NormalizePathCsv(host?.BootstrapIncludePathsCsv);
        var bootstrapExcludePathsCsv = NormalizePathCsv(host?.BootstrapExcludePathsCsv);
        var persistedIncludePathsCsv = NormalizePathCsv(boundPolicy?.IncludePathsCsv);
        var persistedExcludePathsCsv = NormalizePathCsv(boundPolicy?.ExcludePathsCsv);
        var hostLabel = string.IsNullOrWhiteSpace(mappedConfiguration.Hostname) ? configuration.HostId : mappedConfiguration.Hostname!;
        var effectiveName = string.IsNullOrWhiteSpace(boundPolicy?.Name)
            ? $"Politica Inicial - {hostLabel}"
            : boundPolicy!.Name.Trim();
        var effectiveIncludePathsCsv = persistedIncludePathsCsv ?? bootstrapIncludePathsCsv;
        var effectiveExcludePathsCsv = persistedExcludePathsCsv ?? bootstrapExcludePathsCsv;

        return new BootstrapPolicyDraftViewModel
        {
            SuggestedPolicyId = BuildBootstrapPolicyId(configuration.CustomerId, configuration.HostId),
            Name = effectiveName,
            ScopeType = "host",
            CustomerId = configuration.CustomerId,
            HostId = configuration.HostId,
            IncludePathsCsv = effectiveIncludePathsCsv ?? string.Empty,
            ExcludePathsCsv = effectiveExcludePathsCsv,
            AwsRegion = boundPolicy?.AwsRegion ?? awsIntegration.SelectedBucketRegion,
            S3BucketName = boundPolicy?.S3BucketName ?? awsIntegration.SelectedBucket,
            S3KeyPrefix = boundPolicy?.S3KeyPrefix ?? $"{configuration.CustomerId}/{configuration.HostId}",
            ScheduleDaysCsv = boundPolicy?.ScheduleDaysCsv ?? "TUE,FRI",
            ScheduleDays = SplitCsvTokens(boundPolicy?.ScheduleDaysCsv ?? "TUE,FRI"),
            StartTimeLocal = boundPolicy?.StartTimeLocal ?? "22:00",
            MaxRuntimeMinutes = boundPolicy?.MaxRuntimeMinutes ?? 720,
            CpuLimitPercent = boundPolicy?.CpuLimitPercent ?? 35,
            NetworkLimitMbit = boundPolicy?.NetworkLimitMbit ?? 80,
            Enabled = boundPolicy?.Enabled ?? true,
            HasBootstrapPaths = !string.IsNullOrWhiteSpace(effectiveIncludePathsCsv)
        };
    }

    private static BootstrapPolicyDraftViewModel NormalizeBootstrapPolicyDraft(
        BootstrapPolicyDraftViewModel form,
        AgentConfiguration configuration,
        ControlPlane.Api.Domain.Host host)
    {
        var suggestedPolicyId = string.IsNullOrWhiteSpace(form.SuggestedPolicyId)
            ? BuildBootstrapPolicyId(configuration.CustomerId, configuration.HostId)
            : form.SuggestedPolicyId.Trim();

        var name = string.IsNullOrWhiteSpace(form.Name)
            ? $"Politica Inicial - {host.Hostname}"
            : form.Name.Trim();

        var includePathsCsv = NormalizePathCsv(form.IncludePathsCsv);
        var excludePathsCsv = NormalizePathCsv(form.ExcludePathsCsv);

        return new BootstrapPolicyDraftViewModel
        {
            SuggestedPolicyId = suggestedPolicyId,
            Name = name,
            ScopeType = "host",
            CustomerId = configuration.CustomerId,
            HostId = configuration.HostId,
            IncludePathsCsv = includePathsCsv ?? string.Empty,
            ExcludePathsCsv = excludePathsCsv,
            AwsRegion = NormalizeOptionalValue(form.AwsRegion),
            S3BucketName = NormalizeOptionalValue(form.S3BucketName),
            S3KeyPrefix = NormalizePrefix(form.S3KeyPrefix),
            ScheduleDaysCsv = NormalizeScheduleDays(form.ScheduleDays, form.ScheduleDaysCsv, fallback: "TUE,FRI"),
            ScheduleDays = SplitCsvTokens(NormalizeScheduleDays(form.ScheduleDays, form.ScheduleDaysCsv, fallback: "TUE,FRI")),
            StartTimeLocal = NormalizeScheduleTime(form.StartTimeLocal, fallback: "22:00"),
            MaxRuntimeMinutes = ClampPositive(form.MaxRuntimeMinutes, fallback: 720),
            CpuLimitPercent = ClampRange(form.CpuLimitPercent, min: 1, max: 100, fallback: 35),
            NetworkLimitMbit = ClampRange(form.NetworkLimitMbit, min: 1, max: 100_000, fallback: 80),
            Enabled = form.Enabled,
            HasBootstrapPaths = !string.IsNullOrWhiteSpace(includePathsCsv)
        };
    }

    private async Task<AwsIntegrationViewModel> BuildAwsIntegrationViewModelAsync(string? expectedAccountId, string? selectedBucket, CancellationToken ct)
    {
        var discovery = await awsDiscoveryService.DiscoverAsync(expectedAccountId, selectedBucket, ct);
        return new AwsIntegrationViewModel
        {
            IsConnected = discovery.IsConnected,
            ExpectedAccountId = discovery.ExpectedAccountId,
            ResolvedAccountId = discovery.ResolvedAccountId,
            Message = discovery.Message,
            SelectedBucket = discovery.SelectedBucket,
            SelectedBucketRegion = discovery.SelectedBucketRegion,
            CurrentPrefix = null,
            Buckets = discovery.Buckets.Select(b => b.Name).ToArray(),
            Prefixes = discovery.Prefixes
        };
    }

    private static AwsIntegrationViewModel BuildAwsIntegrationViewModelFromAgent(
        string? expectedAccountId,
        string? selectedBucket,
        AgentConfiguration configuration)
    {
        var buckets = SplitCsvTokens(configuration.AvailableBucketsCsv);
        var resolvedAccountId = string.IsNullOrWhiteSpace(configuration.AwsAccountId) ? null : configuration.AwsAccountId.Trim();
        var expected = string.IsNullOrWhiteSpace(expectedAccountId) ? null : expectedAccountId.Trim();
        var accountMatches = string.IsNullOrWhiteSpace(expected) ||
            string.Equals(expected, resolvedAccountId, StringComparison.Ordinal);
        var connected = configuration.PrecheckCredentialOk && accountMatches;
        var message = !configuration.PrecheckCredentialOk
            ? "O Agent ainda nao confirmou uma credencial AWS valida. Consulte o ultimo precheck do host."
            : !accountMatches
                ? $"O Agent reportou a conta AWS {resolvedAccountId ?? "nao identificada"}, mas o cliente espera {expected}."
                : buckets.Length == 0
                    ? "Credencial AWS validada pelo Agent, mas nenhum bucket foi reportado. Confirme a permissao s3:ListAllMyBuckets e aguarde o proximo sincronismo."
                    : $"Credencial AWS validada pelo Agent e {buckets.Length} bucket(s) reportado(s).";

        return new AwsIntegrationViewModel
        {
            IsConnected = connected,
            ExpectedAccountId = expected,
            ResolvedAccountId = resolvedAccountId,
            Message = message,
            SelectedBucket = string.IsNullOrWhiteSpace(selectedBucket) ? buckets.FirstOrDefault() : selectedBucket.Trim(),
            SelectedBucketRegion = null,
            CurrentPrefix = null,
            Buckets = buckets,
            Prefixes = Array.Empty<string>()
        };
    }

    private static HostRunRequestViewModel? BuildLatestRunViewModel(AgentRunRequest? latestRunRequest, Job? latestJob)
    {
        if (latestJob is null || string.Equals(latestJob.Id, latestRunRequest?.JobId, StringComparison.Ordinal))
        {
            return MapRunRequest(latestRunRequest);
        }

        if (latestRunRequest is not null && latestRunRequest.RequestedAtUtc >= latestJob.StartedAtUtc)
        {
            return MapRunRequest(latestRunRequest);
        }

        return new HostRunRequestViewModel
        {
            Id = latestJob.Id,
            State = latestJob.State,
            TriggerType = "scheduled",
            RequestedAtUtc = latestJob.StartedAtUtc,
            ClaimedAtUtc = latestJob.StartedAtUtc,
            CompletedAtUtc = latestJob.FinishedAtUtc,
            JobId = latestJob.Id,
            FailureMessage = latestJob.FailureMessage
        };
    }

    private static HostRunRequestViewModel? MapRunRequest(AgentRunRequest? request)
    {
        if (request is null)
        {
            return null;
        }

        return new HostRunRequestViewModel
        {
            Id = request.Id,
            State = request.State,
            TriggerType = request.TriggerType,
            RequestedAtUtc = request.RequestedAtUtc,
            ClaimedAtUtc = request.ClaimedAtUtc,
            CompletedAtUtc = request.CompletedAtUtc,
            JobId = request.JobId,
            FailureMessage = request.FailureMessage
        };
    }

    private static BackupPolicy? ResolveBoundPolicy(AgentConfiguration configuration, IReadOnlyDictionary<string, BackupPolicy> policies)
    {
        if (!string.IsNullOrWhiteSpace(configuration.PolicyId) &&
            policies.TryGetValue(configuration.PolicyId, out var assignedPolicy))
        {
            return assignedPolicy;
        }

        if (!string.IsNullOrWhiteSpace(configuration.EffectivePolicyId) &&
            policies.TryGetValue(configuration.EffectivePolicyId, out var effectivePolicy))
        {
            return effectivePolicy;
        }

        return null;
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizePrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Trim('/');
    }

    private static string BuildBootstrapPolicyId(string customerId, string hostId)
    {
        var input = $"bootstrap-policy\n{customerId.Trim()}\n{hostId.Trim()}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return "policy-bootstrap-" + hex[..40];
    }

    private static string NormalizePolicyKind(string? policyKind, string fallback)
    {
        if (string.Equals(policyKind, "bootstrap", StringComparison.OrdinalIgnoreCase))
        {
            return "bootstrap";
        }

        if (string.Equals(policyKind, "operational", StringComparison.OrdinalIgnoreCase))
        {
            return "operational";
        }

        return fallback;
    }

    private static PolicyChangeEvent BuildPolicyChangeEvent(string policyId, string eventType, string message)
    {
        return new PolicyChangeEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            PolicyId = policyId,
            EventType = eventType,
            Message = message,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static string BuildPolicyUpdateMessage(string previousKind, string newKind, bool previousEnabled, bool newEnabled)
    {
        if (!string.Equals(previousKind, newKind, StringComparison.OrdinalIgnoreCase))
        {
            return $"Politica alterada de {previousKind} para {newKind}.";
        }

        if (previousEnabled != newEnabled)
        {
            return newEnabled ? "Politica reativada." : "Politica desativada.";
        }

        return "Parametros operacionais da politica foram atualizados.";
    }

    private static (string Label, string CssClass, string Message, bool PolicyExists, bool IsAssigned, string? PolicyId) DescribeBootstrapState(
        string customerId,
        string hostId,
        string? bootstrapIncludePathsCsv,
        string? assignedPolicyId,
        IReadOnlyDictionary<string, BackupPolicy> policies)
    {
        var hasBootstrapPaths = !string.IsNullOrWhiteSpace(NormalizePathCsv(bootstrapIncludePathsCsv));
        if (!hasBootstrapPaths)
        {
            return ("Sem bootstrap", "offline", "O Agent ainda nao reportou paths iniciais para este host.", false, false, null);
        }

        var bootstrapPolicyId = BuildBootstrapPolicyId(customerId, hostId);
        var bootstrapPolicyExists = policies.ContainsKey(bootstrapPolicyId);
        var bootstrapAssigned = string.Equals(assignedPolicyId, bootstrapPolicyId, StringComparison.OrdinalIgnoreCase);
        var hasAnyAssignedPolicy = !string.IsNullOrWhiteSpace(assignedPolicyId);

        if (bootstrapAssigned)
        {
            return ("Promovido", "ready", "A politica bootstrap ja foi aprovada e esta vinculada ao host.", true, true, bootstrapPolicyId);
        }

        if (hasAnyAssignedPolicy)
        {
            return (
                "Customizado",
                "ready",
                bootstrapPolicyExists
                    ? "O host ja usa uma politica diferente da bootstrap inicial."
                    : "O host ja usa uma politica operacional; o bootstrap inicial nao precisa mais ser promovido.",
                bootstrapPolicyExists,
                false,
                bootstrapPolicyId);
        }

        if (bootstrapPolicyExists)
        {
            return ("Aguardando vinculo", "warning", "A politica bootstrap ja existe no painel e aguarda vinculacao ao host.", true, false, bootstrapPolicyId);
        }

        return ("Pendente aprovacao", "warning", "O host reportou paths bootstrap e aguarda aprovacao da politica inicial.", false, false, bootstrapPolicyId);
    }

    private static string? NormalizePathCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return null;
        }

        var values = csv
            .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? null : string.Join(";", values);
    }

    private static string NormalizeUpperTokenCsv(string? csv, string fallback)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return fallback;
        }

        var values = csv
            .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return values.Length == 0 ? fallback : string.Join(",", values);
    }

    private static string NormalizeScheduleDays(IReadOnlyList<string>? selectedDays, string? csv, string fallback)
    {
        if (selectedDays is not null && selectedDays.Count > 0)
        {
            return NormalizeUpperTokenCsv(string.Join(",", selectedDays), fallback);
        }

        return NormalizeUpperTokenCsv(csv, fallback);
    }

    private static string[] SplitCsvTokens(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<string>();
        }

        return csv
            .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToUpperInvariant())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeScheduleTime(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var parts = value.Trim().Split(':');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out var hour) ||
            !int.TryParse(parts[1], out var minute))
        {
            return fallback;
        }

        hour = Math.Clamp(hour, 0, 23);
        minute = Math.Clamp(minute, 0, 59);
        return $"{hour:00}:{minute:00}";
    }

    private static int ClampPositive(int value, int fallback) => value > 0 ? value : fallback;

    private static int ClampRange(int value, int min, int max, int fallback)
    {
        if (value < min || value > max)
        {
            return fallback;
        }

        return value;
    }

    private Task RecordAuditAsync(
        string category,
        string action,
        string entityType,
        string? entityId,
        string message,
        string? customerId,
        string? hostId,
        CancellationToken ct,
        string outcome = "success",
        IReadOnlyDictionary<string, string?>? metadata = null)
    {
        return auditTrailService.RecordAsync(
            HttpContext,
            category,
            action,
            entityType,
            entityId,
            message,
            customerId,
            hostId,
            outcome,
            metadata,
            ct);
    }

    private IActionResult RedirectToLocalOrDefault(string? returnUrl, string defaultPath)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect(defaultPath);
    }

    private static readonly System.Text.RegularExpressions.Regex AgentIdentifierRegex =
        new("^[a-zA-Z0-9][a-zA-Z0-9._-]{1,127}$", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool IsAgentCompatibleIdentifier(string value)
    {
        return !string.IsNullOrWhiteSpace(value) && AgentIdentifierRegex.IsMatch(value.Trim());
    }

    private static void AddSignal(
        ICollection<OperationalHealthSignalViewModel> signals,
        string name,
        string statusLabel,
        string statusCssClass,
        string detail)
    {
        signals.Add(new OperationalHealthSignalViewModel
        {
            Name = name,
            StatusLabel = statusLabel,
            StatusCssClass = statusCssClass,
            Detail = detail
        });
    }

    private static void AddRecoveryStep(
        ICollection<OperationalRecoveryStepViewModel> steps,
        bool condition,
        string severity,
        string title,
        string message,
        string? linkText,
        string? linkUrl)
    {
        if (!condition)
        {
            return;
        }

        steps.Add(new OperationalRecoveryStepViewModel
        {
            Severity = severity,
            Title = title,
            Message = message,
            LinkText = linkText,
            LinkUrl = linkUrl
        });
    }

    private static (string? LinkText, string? LinkUrl) BuildAlertSuggestedAction(
        Alert alert,
        ControlPlane.Api.Domain.Host host,
        AgentConfiguration? configuration,
        HostOperationalHealthViewModel? health)
    {
        if (!string.IsNullOrWhiteSpace(alert.JobId))
        {
            return ("Abrir job", $"/admin/jobs/{Uri.EscapeDataString(alert.JobId)}");
        }

        if (health is not null && string.Equals(health.RiskLevelLabel, "Critico", StringComparison.OrdinalIgnoreCase))
        {
            if (configuration is not null)
            {
                return ("Abrir configuracao", $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}");
            }

            return ("Abrir suporte do host", $"/admin/hosts/{Uri.EscapeDataString(host.Id)}/edit");
        }

        if (configuration is not null)
        {
            return ("Revisar configuracao", $"/admin/configurations/{Uri.EscapeDataString(configuration.Id)}");
        }

        return ("Abrir host", $"/admin/hosts/{Uri.EscapeDataString(host.Id)}/edit");
    }

    private static string MapHeartbeatCssClass(string heartbeatStatus)
    {
        if (string.Equals(heartbeatStatus, "Online", StringComparison.OrdinalIgnoreCase))
        {
            return "ready";
        }

        if (string.Equals(heartbeatStatus, "Atrasado", StringComparison.OrdinalIgnoreCase))
        {
            return "warning";
        }

        return string.Equals(heartbeatStatus, "Offline", StringComparison.OrdinalIgnoreCase) ? "offline" : "not-ready";
    }

    private static string MapServiceStatusCssClass(string? serviceStatus)
    {
        if (IsRunningLike(serviceStatus))
        {
            return "ready";
        }

        if (string.IsNullOrWhiteSpace(serviceStatus))
        {
            return "not-ready";
        }

        return "critical";
    }

    private static bool IsRunningLike(string? serviceStatus)
    {
        return string.Equals(serviceStatus, "Running", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(serviceStatus, "DryRun", StringComparison.OrdinalIgnoreCase);
    }

    private static string SafePrecheckMessage(AgentConfiguration configuration, string fallback)
    {
        if (string.IsNullOrWhiteSpace(configuration.LastPrecheckMessage))
        {
            return fallback;
        }

        var message = configuration.LastPrecheckMessage.Trim();
        var policySourceIndex = message.IndexOf(" PolicySource=", StringComparison.OrdinalIgnoreCase);
        if (policySourceIndex >= 0)
        {
            message = message[..policySourceIndex].Trim();
        }

        return string.IsNullOrWhiteSpace(message) ? fallback : message;
    }
}
