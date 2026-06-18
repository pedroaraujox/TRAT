using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
public sealed class AdminController(AppDbContext db, PolicyResolutionService policyResolutionService) : ControllerBase
{
    public sealed record CustomerUpsertRequest(
        string Id,
        string Name,
        string AwsAccountId,
        string? NotificationEmailsCsv,
        DateTimeOffset? CreatedAtUtc
    );

    public sealed record PolicyUpsertRequest(
        string Id,
        string CustomerId,
        string Name,
        string ScopeType,
        string? HostId,
        string IncludePathsCsv,
        string? ExcludePathsCsv,
        string ScheduleDaysCsv,
        string StartTimeLocal,
        int MaxRuntimeMinutes,
        int CpuLimitPercent,
        int NetworkLimitMbit,
        bool Enabled,
        DateTimeOffset? CreatedAtUtc
    );

    [HttpGet("customers")]
    public async Task<IActionResult> Customers([FromQuery] int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var customers = await db.Customers.OrderBy(c => c.Name).Take(take).ToListAsync(ct);
        return Ok(customers);
    }

    [HttpPost("customers")]
    public async Task<IActionResult> UpsertCustomer([FromBody] CustomerUpsertRequest customer, CancellationToken ct)
    {
        if (customer is null)
        {
            return BadRequest("Body inválido.");
        }

        if (string.IsNullOrWhiteSpace(customer.Id) || string.IsNullOrWhiteSpace(customer.Name) || string.IsNullOrWhiteSpace(customer.AwsAccountId))
        {
            return BadRequest("Id, Name e AwsAccountId são obrigatórios.");
        }

        var customerId = customer.Id.Trim();
        var existing = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (existing is null)
        {
            db.Customers.Add(new Customer
            {
                Id = customerId,
                Name = customer.Name.Trim(),
                AwsAccountId = customer.AwsAccountId.Trim(),
                NotificationEmailsCsv = string.IsNullOrWhiteSpace(customer.NotificationEmailsCsv) ? null : customer.NotificationEmailsCsv.Trim(),
                CreatedAtUtc = customer.CreatedAtUtc ?? DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Name = customer.Name.Trim();
            existing.AwsAccountId = customer.AwsAccountId.Trim();
            existing.NotificationEmailsCsv = string.IsNullOrWhiteSpace(customer.NotificationEmailsCsv) ? null : customer.NotificationEmailsCsv.Trim();
        }

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpGet("hosts")]
    public async Task<IActionResult> Hosts([FromQuery] string? customerId = null, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var q = db.Hosts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            q = q.Where(h => h.CustomerId == customerId);
        }

        var hosts = (await q.ToListAsync(ct))
            .OrderByDescending(h => h.LastHeartbeatAtUtc)
            .Take(take)
            .ToList();
        return Ok(hosts);
    }

    [HttpGet("jobs")]
    public async Task<IActionResult> Jobs([FromQuery] string? customerId = null, [FromQuery] string? hostId = null, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var q = db.Jobs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            q = q.Where(j => j.CustomerId == customerId);
        }
        if (!string.IsNullOrWhiteSpace(hostId))
        {
            q = q.Where(j => j.HostId == hostId);
        }

        var jobs = (await q.ToListAsync(ct))
            .OrderByDescending(j => j.StartedAtUtc)
            .Take(take)
            .ToList();
        return Ok(jobs);
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> Alerts([FromQuery] string? customerId = null, [FromQuery] int take = 200, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var q = db.Alerts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            q = q.Where(a => a.CustomerId == customerId);
        }

        var alerts = (await q.ToListAsync(ct))
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(take)
            .ToList();
        return Ok(alerts);
    }

    [HttpGet("configurations")]
    public async Task<IActionResult> Configurations(
        [FromQuery] string? customerId = null,
        [FromQuery] string? hostId = null,
        [FromQuery] string? serviceStatus = null,
        [FromQuery] int take = 200,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 1000);
        var q = db.AgentConfigurations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            q = q.Where(c => c.CustomerId == customerId);
        }
        if (!string.IsNullOrWhiteSpace(hostId))
        {
            q = q.Where(c => c.HostId == hostId);
        }
        if (!string.IsNullOrWhiteSpace(serviceStatus))
        {
            q = q.Where(c => c.ServiceStatus == serviceStatus);
        }

        var configs = (await q.ToListAsync(ct))
            .OrderByDescending(c => c.LastConfigSyncAtUtc ?? DateTimeOffset.MinValue)
            .ThenByDescending(c => c.CreatedAtUtc)
            .Take(take)
            .ToList();

        return Ok(configs);
    }

    [HttpGet("effective-policy")]
    public async Task<IActionResult> EffectivePolicy([FromQuery] string customerId, [FromQuery] string hostId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(hostId))
        {
            return BadRequest("CustomerId e HostId são obrigatórios.");
        }

        var response = await policyResolutionService.ResolveEffectivePolicyAsync(customerId, hostId, ct);
        return Ok(response);
    }

    [HttpPost("policies")]
    public async Task<IActionResult> UpsertPolicy([FromBody] PolicyUpsertRequest request, CancellationToken ct)
    {
        if (request is null)
        {
            return BadRequest("Body inválido.");
        }

        if (string.IsNullOrWhiteSpace(request.Id) ||
            string.IsNullOrWhiteSpace(request.CustomerId) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.ScopeType) ||
            string.IsNullOrWhiteSpace(request.IncludePathsCsv) ||
            string.IsNullOrWhiteSpace(request.ScheduleDaysCsv) ||
            string.IsNullOrWhiteSpace(request.StartTimeLocal))
        {
            return BadRequest("Id, CustomerId, Name, ScopeType, IncludePathsCsv, ScheduleDaysCsv e StartTimeLocal são obrigatórios.");
        }

        var scopeType = request.ScopeType.Trim();
        if (!string.Equals(scopeType, "customer", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(scopeType, "host", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("ScopeType deve ser 'customer' ou 'host'.");
        }

        if (string.Equals(scopeType, "host", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(request.HostId))
        {
            return BadRequest("HostId é obrigatório para políticas de host.");
        }

        if (request.MaxRuntimeMinutes <= 0 || request.CpuLimitPercent <= 0 || request.NetworkLimitMbit <= 0)
        {
            return BadRequest("MaxRuntimeMinutes, CpuLimitPercent e NetworkLimitMbit devem ser maiores que zero.");
        }

        var policyId = request.Id.Trim();
        var existing = await db.BackupPolicies.FirstOrDefaultAsync(p => p.Id == policyId, ct);
        if (existing is null)
        {
            db.BackupPolicies.Add(new BackupPolicy
            {
                Id = policyId,
                CustomerId = request.CustomerId.Trim(),
                Name = request.Name.Trim(),
                PolicyKind = "operational",
                ScopeType = scopeType.ToLowerInvariant(),
                HostId = string.IsNullOrWhiteSpace(request.HostId) ? null : request.HostId.Trim(),
                OriginHostId = null,
                IncludePathsCsv = request.IncludePathsCsv.Trim(),
                ExcludePathsCsv = string.IsNullOrWhiteSpace(request.ExcludePathsCsv) ? null : request.ExcludePathsCsv.Trim(),
                ScheduleDaysCsv = request.ScheduleDaysCsv.Trim().ToUpperInvariant(),
                StartTimeLocal = request.StartTimeLocal.Trim(),
                MaxRuntimeMinutes = request.MaxRuntimeMinutes,
                CpuLimitPercent = request.CpuLimitPercent,
                NetworkLimitMbit = request.NetworkLimitMbit,
                Enabled = request.Enabled,
                LastChangedAtUtc = request.CreatedAtUtc ?? DateTimeOffset.UtcNow,
                CreatedAtUtc = request.CreatedAtUtc ?? DateTimeOffset.UtcNow
            });

            db.PolicyChangeEvents.Add(new PolicyChangeEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                PolicyId = policyId,
                EventType = "policy_created_api",
                Message = "Politica operacional criada via endpoint administrativo.",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.CustomerId = request.CustomerId.Trim();
            existing.Name = request.Name.Trim();
            existing.PolicyKind = "operational";
            existing.ScopeType = scopeType.ToLowerInvariant();
            existing.HostId = string.IsNullOrWhiteSpace(request.HostId) ? null : request.HostId.Trim();
            existing.OriginHostId = null;
            existing.IncludePathsCsv = request.IncludePathsCsv.Trim();
            existing.ExcludePathsCsv = string.IsNullOrWhiteSpace(request.ExcludePathsCsv) ? null : request.ExcludePathsCsv.Trim();
            existing.ScheduleDaysCsv = request.ScheduleDaysCsv.Trim().ToUpperInvariant();
            existing.StartTimeLocal = request.StartTimeLocal.Trim();
            existing.MaxRuntimeMinutes = request.MaxRuntimeMinutes;
            existing.CpuLimitPercent = request.CpuLimitPercent;
            existing.NetworkLimitMbit = request.NetworkLimitMbit;
            existing.Enabled = request.Enabled;
            existing.LastChangedAtUtc = DateTimeOffset.UtcNow;

            db.PolicyChangeEvents.Add(new PolicyChangeEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                PolicyId = policyId,
                EventType = "policy_updated_api",
                Message = "Politica operacional atualizada via endpoint administrativo.",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        return Ok();
    }
}
