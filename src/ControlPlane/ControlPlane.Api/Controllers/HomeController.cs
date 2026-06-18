using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Models;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace ControlPlane.Api.Controllers;

public sealed class HomeController(
    AppDbContext db,
    IConfiguration config,
    HostOperationalStatusService hostOperationalStatusService,
    ILogger<HomeController> logger) : Controller
{
    private const string EnrollmentTokenSessionKeyPrefix = "customer-enroll-token:";

    [HttpGet("/")]
    public IActionResult Root() => Redirect("/admin");

    [HttpGet("/login")]
    public IActionResult Login() => View(new LoginViewModel());

    [HttpPost("/login")]
    [ValidateAntiForgeryToken]
    public IActionResult LoginPost([FromForm] string token)
    {
        var expected = config["ControlPlane:Security:AdminToken"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(expected, token, StringComparison.Ordinal))
        {
            return View("Login", new LoginViewModel
            {
                ErrorMessage = "Token administrativo invalido."
            });
        }

        HttpContext.Session.SetString("admin-token", token);
        return Redirect("/admin");
    }

    [HttpPost("/logout")]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Remove("admin-token");
        return Redirect("/login");
    }

    [HttpGet("/admin")]
    public async Task<IActionResult> Dashboard(CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);
        var customersById = customers.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        var policiesById = await db.BackupPolicies.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p, ct);
        var configsByHostId = (await db.AgentConfigurations.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.HostId)
            .Select(g => g.OrderByDescending(x => x.LastConfigSyncAtUtc ?? x.CreatedAtUtc).FirstOrDefault()!)
            .ToDictionary(c => c.HostId, c => c, StringComparer.OrdinalIgnoreCase);
        var hosts = (await db.Hosts.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(h => h.LastHeartbeatAtUtc)
            .Take(20)
            .ToList();
        var hostsById = hosts.ToDictionary(h => h.Id, StringComparer.OrdinalIgnoreCase);
        var jobs = (await db.Jobs.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(j => j.StartedAtUtc)
            .Take(20)
            .ToList();
        var allJobs = await db.Jobs.AsNoTracking().ToListAsync(ct);
        var alerts = (await db.Alerts.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(a => a.CreatedAtUtc)
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
                ActiveAlertCount = await db.Alerts.CountAsync(a => a.AcknowledgedAtUtc == null, ct),
                FailedJobsLast7Days = allJobs.Count(j => j.State == "FAILED" && j.StartedAtUtc >= DateTimeOffset.UtcNow.AddDays(-7))
            },
            Customers = customerCards,
            Hosts = hosts.Select(h => MapHost(h, customersById, configsByHostId, policiesById)).ToArray(),
            RecentJobs = jobs.Select(j => MapJob(j, customersById, hostsById)).ToArray(),
            Alerts = alerts.Select(a => MapAlert(a, customersById, hostsById)).ToArray()
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
            .Where(a => a.AcknowledgedAtUtc == null)
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
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(30)
            .ToList();
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
            Hosts = hosts.Select(h => MapHost(h, customerMap, configsByHostId, policyMap)).ToArray(),
            Jobs = jobs.Select(j => MapJob(j, customerMap, hostsById)).ToArray(),
            Alerts = alerts.Select(a => MapAlert(a, customerMap, hostsById)).ToArray(),
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

        HttpContext.Session.SetString(BuildEnrollmentTokenSessionKey(customerId), enrollmentToken);
        return Redirect($"/admin/customers/{Uri.EscapeDataString(customerId)}");
    }

    private static string BuildEnrollmentTokenSessionKey(string customerId)
    {
        return EnrollmentTokenSessionKeyPrefix + customerId.Trim().ToLowerInvariant();
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
        return Redirect($"/admin/customers/{Uri.EscapeDataString(id)}");
    }

    [HttpGet("/admin/hosts")]
    public async Task<IActionResult> Hosts([FromQuery] string? customerId, [FromQuery] string? status, CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var policies = await db.BackupPolicies.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p, ct);
        var configsByHostId = (await db.AgentConfigurations.AsNoTracking().ToListAsync(ct))
            .GroupBy(c => c.HostId)
            .Select(g => g.OrderByDescending(x => x.LastConfigSyncAtUtc ?? x.CreatedAtUtc).FirstOrDefault()!)
            .ToDictionary(c => c.HostId, c => c, StringComparer.OrdinalIgnoreCase);
        var hosts = (await db.Hosts.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(h => h.LastHeartbeatAtUtc)
            .ToList();
        var rows = hosts.Select(h => MapHost(h, customers, configsByHostId, policies)).ToArray();

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

        return View(new HostsPageViewModel
        {
            CustomerId = customerId,
            Status = status,
            Hosts = rows
        });
    }

    [HttpGet("/admin/hosts/new")]
    public IActionResult NewHost()
    {
        return View("HostForm", new HostFormViewModel
        {
            Id = string.Empty,
            CustomerId = string.Empty,
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
            ScopeType = "customer",
            IncludePathsCsv = string.Empty,
            ExcludePathsCsv = string.Empty,
            ScheduleDaysCsv = "TUE,FRI",
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
        var error = ValidatePolicyForm(form, false);
        if (error is not null)
        {
            return View("PolicyForm", form with { ErrorMessage = error, IsEditMode = false });
        }

        var exists = await db.BackupPolicies.AnyAsync(p => p.Id == form.Id.Trim(), ct);
        if (exists)
        {
            return View("PolicyForm", form with { ErrorMessage = "Ja existe uma politica com este ID.", IsEditMode = false });
        }

        db.BackupPolicies.Add(new BackupPolicy
        {
            Id = form.Id.Trim(),
            CustomerId = form.CustomerId.Trim(),
            HostId = string.IsNullOrWhiteSpace(form.HostId) ? null : form.HostId.Trim(),
            Name = form.Name.Trim(),
            ScopeType = form.ScopeType.Trim(),
            IncludePathsCsv = form.IncludePathsCsv.Trim(),
            ExcludePathsCsv = string.IsNullOrWhiteSpace(form.ExcludePathsCsv) ? null : form.ExcludePathsCsv.Trim(),
            ScheduleDaysCsv = form.ScheduleDaysCsv.Trim(),
            StartTimeLocal = form.StartTimeLocal.Trim(),
            MaxRuntimeMinutes = form.MaxRuntimeMinutes,
            CpuLimitPercent = form.CpuLimitPercent,
            NetworkLimitMbit = form.NetworkLimitMbit,
            Enabled = form.Enabled,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(ct);
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

        return View("PolicyForm", new PolicyFormViewModel
        {
            OriginalId = policy.Id,
            Id = policy.Id,
            CustomerId = policy.CustomerId,
            HostId = policy.HostId,
            Name = policy.Name,
            ScopeType = policy.ScopeType,
            IncludePathsCsv = policy.IncludePathsCsv,
            ExcludePathsCsv = policy.ExcludePathsCsv,
            ScheduleDaysCsv = policy.ScheduleDaysCsv,
            StartTimeLocal = policy.StartTimeLocal,
            MaxRuntimeMinutes = policy.MaxRuntimeMinutes,
            CpuLimitPercent = policy.CpuLimitPercent,
            NetworkLimitMbit = policy.NetworkLimitMbit,
            Enabled = policy.Enabled,
            IsEditMode = true
        });
    }

    [HttpPost("/admin/policies/{id}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPolicyPost(string id, [FromForm] PolicyFormViewModel form, CancellationToken ct)
    {
        var error = ValidatePolicyForm(form, true);
        if (error is not null)
        {
            return View("PolicyForm", form with { ErrorMessage = error, OriginalId = id, IsEditMode = true });
        }

        var policy = await db.BackupPolicies.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (policy is null)
        {
            return NotFound();
        }

        policy.CustomerId = form.CustomerId.Trim();
        policy.HostId = string.IsNullOrWhiteSpace(form.HostId) ? null : form.HostId.Trim();
        policy.Name = form.Name.Trim();
        policy.ScopeType = form.ScopeType.Trim();
        policy.IncludePathsCsv = form.IncludePathsCsv.Trim();
        policy.ExcludePathsCsv = string.IsNullOrWhiteSpace(form.ExcludePathsCsv) ? null : form.ExcludePathsCsv.Trim();
        policy.ScheduleDaysCsv = form.ScheduleDaysCsv.Trim();
        policy.StartTimeLocal = form.StartTimeLocal.Trim();
        policy.MaxRuntimeMinutes = form.MaxRuntimeMinutes;
        policy.CpuLimitPercent = form.CpuLimitPercent;
        policy.NetworkLimitMbit = form.NetworkLimitMbit;
        policy.Enabled = form.Enabled;

        await db.SaveChangesAsync(ct);
        return Redirect("/admin/policies");
    }

    [HttpGet("/admin/configurations")]
    public async Task<IActionResult> Configurations([FromQuery] string? customerId, [FromQuery] string? serviceStatus, CancellationToken ct)
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

        return View(new AgentConfigurationsPageViewModel
        {
            CustomerId = customerId,
            ServiceStatus = serviceStatus,
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

        TempData["StatusMessage"] = trimmedPolicyId is null
            ? "Politica desvinculada com sucesso."
            : "Politica vinculada com sucesso.";
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
    public async Task<IActionResult> Alerts([FromQuery] string? customerId, [FromQuery] string? severity, [FromQuery] string? status, CancellationToken ct)
    {
        var customers = await db.Customers.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c, ct);
        var hosts = await db.Hosts.AsNoTracking().ToDictionaryAsync(h => h.Id, h => h, ct);
        var alerts = (await db.Alerts.AsNoTracking().ToListAsync(ct))
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToList();
        var rows = alerts.Select(a => MapAlert(a, customers, hosts)).ToArray();

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
            rows = rows.Where(a => a.AcknowledgedAtUtc is null).ToArray();
        }
        else if (string.Equals(status, "acknowledged", StringComparison.OrdinalIgnoreCase))
        {
            rows = rows.Where(a => a.AcknowledgedAtUtc is not null).ToArray();
        }

        return View(new AlertsPageViewModel
        {
            CustomerId = customerId,
            Severity = severity,
            Status = status,
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

        alert.AcknowledgedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Alert {AlertId} acknowledged for customer {CustomerId}", id, alert.CustomerId);

        TempData["StatusMessage"] = "Alerta reconhecido com sucesso.";
        return RedirectToLocalOrDefault(returnUrl, "/admin/alerts");
    }

    private static string? ValidateCustomerForm(CustomerFormViewModel form, bool isEditMode)
    {
        if (!isEditMode && string.IsNullOrWhiteSpace(form.Id))
        {
            return "ID do cliente e obrigatorio.";
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
        if (!isEditMode && !IsSafeIdentifier(form.Id))
        {
            return "ID do host deve conter apenas letras, numeros, ponto, hifen ou underscore.";
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
        IReadOnlyDictionary<string, BackupPolicy> policiesById)
    {
        customers.TryGetValue(host.CustomerId, out var customer);
        configsByHostId.TryGetValue(host.Id, out var config);
        BackupPolicy? policy = null;
        if (config?.PolicyId is not null)
        {
            policiesById.TryGetValue(config.PolicyId, out policy);
        }

        var operational = hostOperationalStatusService.Evaluate(host, config);

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
            ServiceStatus = config?.ServiceStatus,
            AssignedPolicyName = policy?.Name,
            PrecheckTlsOk = config?.PrecheckTlsOk,
            PrecheckDiskOk = config?.PrecheckDiskOk,
            PrecheckCredentialOk = config?.PrecheckCredentialOk,
            OperationalStatusLabel = operational.Label,
            OperationalStatusCssClass = operational.CssClass,
            OperationalStatusMessage = operational.Message,
            IsReadyForPolicyAssignment = operational.IsReadyForPolicyAssignment
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

    private static AlertRowViewModel MapAlert(Alert alert, IReadOnlyDictionary<string, Customer> customers, IReadOnlyDictionary<string, ControlPlane.Api.Domain.Host> hosts)
    {
        customers.TryGetValue(alert.CustomerId, out var customer);
        if (alert.HostId is not null)
        {
            hosts.TryGetValue(alert.HostId, out var host);
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
                CreatedAtUtc = alert.CreatedAtUtc,
                AcknowledgedAtUtc = alert.AcknowledgedAtUtc
            };
        }

        return new AlertRowViewModel
        {
            Id = alert.Id,
            CustomerId = alert.CustomerId,
            CustomerName = customer?.Name,
            HostId = alert.HostId,
            Hostname = null,
            JobId = alert.JobId,
            Type = alert.Type,
            Severity = alert.Severity,
            Message = alert.Message,
            CreatedAtUtc = alert.CreatedAtUtc,
            AcknowledgedAtUtc = alert.AcknowledgedAtUtc
        };
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
            ScopeType = policy.ScopeType,
            IncludePathsCsv = policy.IncludePathsCsv,
            ExcludePathsCsv = policy.ExcludePathsCsv,
            ScheduleDaysCsv = policy.ScheduleDaysCsv,
            StartTimeLocal = policy.StartTimeLocal,
            MaxRuntimeMinutes = policy.MaxRuntimeMinutes,
            CpuLimitPercent = policy.CpuLimitPercent,
            NetworkLimitMbit = policy.NetworkLimitMbit,
            Enabled = policy.Enabled
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

        return new AgentConfigurationListItemViewModel
        {
            Id = configuration.Id,
            CustomerId = configuration.CustomerId,
            CustomerName = customer?.Name,
            HostId = configuration.HostId,
            Hostname = host?.Hostname,
            PolicyId = configuration.PolicyId,
            PolicyName = policy?.Name,
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
            IsReadyForPolicyAssignment = operational?.IsReadyForPolicyAssignment ?? false
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
            AssignedPolicyName = policy?.Name,
            OperationalStatusLabel = operational.Label,
            OperationalStatusCssClass = operational.CssClass,
            OperationalStatusMessage = operational.Message,
            BootstrapIncludePathsCsv = host.BootstrapIncludePathsCsv,
            BootstrapExcludePathsCsv = host.BootstrapExcludePathsCsv,
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

        return new AgentConfigurationDetailViewModel
        {
            Configuration = MapAgentConfiguration(configEntity, customers, hosts, policies),
            RecentJobs = jobs.Select(j => MapJob(j, customers, hosts)).ToArray(),
            PolicyOptions = await BuildPolicyOptionsAsync(configEntity.CustomerId, configEntity.HostId, ct)
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

    private IActionResult RedirectToLocalOrDefault(string? returnUrl, string defaultPath)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect(defaultPath);
    }

    private static bool IsSafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Trim().All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');
    }
}
