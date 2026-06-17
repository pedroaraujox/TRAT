using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
public sealed class AdminController(AppDbContext db) : ControllerBase
{
    public sealed record CustomerUpsertRequest(
        string Id,
        string Name,
        string AwsAccountId,
        string? NotificationEmailsCsv,
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
}
