using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Dtos;
using ControlPlane.Api.Email;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace ControlPlane.Api.Controllers;

[ApiController]
[Route("api/v1/agents")]
public sealed class AgentIngestController(AppDbContext db, SmtpEmailSender email, ILogger<AgentIngestController> logger) : ControllerBase
{
    [HttpGet("/api/v1/health")]
    public IActionResult Health() => Ok(new { status = "ok" });

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] AgentHeartbeatRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.HostId))
        {
            return BadRequest("CustomerId e HostId são obrigatórios.");
        }

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.CustomerId == request.CustomerId && h.Id == request.HostId, ct);
        if (host is null)
        {
            host = new ControlPlane.Api.Domain.Host
            {
                Id = request.HostId,
                CustomerId = request.CustomerId,
                Hostname = request.Hostname,
                OsVersion = request.OsVersion,
                FirstSeenAtUtc = DateTimeOffset.UtcNow,
                LastHeartbeatAtUtc = request.TimestampUtc
            };
            db.Hosts.Add(host);
        }
        else
        {
            host.LastHeartbeatAtUtc = request.TimestampUtc;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Heartbeat recebido: customer={CustomerId} host={HostId} os={OsVersion}", request.CustomerId, request.HostId, request.OsVersion);
        return Ok();
    }

    [HttpPost("jobs/start")]
    public async Task<IActionResult> JobStart([FromBody] JobStartRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.HostId) || string.IsNullOrWhiteSpace(request.JobId))
        {
            return BadRequest("CustomerId, HostId e JobId são obrigatórios.");
        }

        var exists = await db.Jobs.AnyAsync(j => j.CustomerId == request.CustomerId && j.HostId == request.HostId && j.Id == request.JobId, ct);
        if (exists)
        {
            return Conflict("JobId já existe para este host.");
        }

        db.Jobs.Add(new Job
        {
            Id = request.JobId,
            CustomerId = request.CustomerId,
            HostId = request.HostId,
            State = "STARTED",
            StartedAtUtc = request.StartedAtUtc
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Job iniciado: customer={CustomerId} host={HostId} job={JobId}", request.CustomerId, request.HostId, request.JobId);
        return Ok();
    }

    [HttpPost("jobs/progress")]
    public async Task<IActionResult> JobProgress([FromBody] JobProgressReport report, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.CustomerId == report.CustomerId && j.HostId == report.HostId && j.Id == report.JobId, ct);
        if (job is null)
        {
            return NotFound("Job não encontrado.");
        }

        job.State = report.State;
        job.PlannedBytes = report.PlannedBytes;
        job.PlannedItems = report.PlannedItems;
        job.UploadedBytes = report.UploadedBytes;
        job.UploadedItems = report.UploadedItems;

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("jobs/final")]
    public async Task<IActionResult> JobFinal([FromBody] JobFinalReport report, CancellationToken ct)
    {
        var job = await db.Jobs.FirstOrDefaultAsync(j => j.CustomerId == report.CustomerId && j.HostId == report.HostId && j.Id == report.JobId, ct);
        if (job is null)
        {
            return NotFound("Job não encontrado.");
        }

        job.State = report.FinalState;
        job.FinishedAtUtc = report.FinishedAtUtc;
        job.PlannedBytes = report.PlannedBytes;
        job.PlannedItems = report.PlannedItems;
        job.UploadedBytes = report.UploadedBytes;
        job.UploadedItems = report.UploadedItems;
        job.FailureCode = report.FailureCode;
        job.FailureMessage = report.FailureMessage;

        foreach (var a in report.Artifacts)
        {
            db.Artifacts.Add(new Artifact
            {
                Id = Guid.NewGuid().ToString("N"),
                JobId = job.Id,
                Type = a.Type,
                Location = a.Location,
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
        if (!string.Equals(report.FinalState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            db.Alerts.Add(new Alert
            {
                Id = Guid.NewGuid().ToString("N"),
                CustomerId = report.CustomerId,
                HostId = report.HostId,
                JobId = report.JobId,
                Type = "JOB_FAILED",
                Severity = "CRITICAL",
                Message = $"{report.FailureCode ?? "FAILED"}: {report.FailureMessage ?? "Job finalizado com falha."}",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == report.CustomerId, ct);
        if (customer is not null)
        {
            var recipients = ParseEmails(customer.NotificationEmailsCsv);
            var subject = $"Backup {report.FinalState} - Cliente {customer.Name} - Host {report.HostId}";
            var body =
                $"JobId: {report.JobId}\n" +
                $"Cliente: {customer.Name} ({customer.Id})\n" +
                $"Host: {report.HostId}\n" +
                $"FinalState: {report.FinalState}\n" +
                $"PlannedBytes: {report.PlannedBytes}\n" +
                $"PlannedItems: {report.PlannedItems}\n" +
                $"UploadedBytes: {report.UploadedBytes}\n" +
                $"UploadedItems: {report.UploadedItems}\n" +
                $"FailureCode: {report.FailureCode}\n" +
                $"FailureMessage: {report.FailureMessage}\n" +
                $"FinishedAtUtc: {report.FinishedAtUtc:O}\n";

            foreach (var r in recipients)
            {
                await email.TrySendAsync(r, subject, body, ct);
            }
        }

        logger.LogInformation("Job finalizado: customer={CustomerId} host={HostId} job={JobId} state={State}", report.CustomerId, report.HostId, report.JobId, report.FinalState);
        return Ok();
    }

    private static IReadOnlyList<string> ParseEmails(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return Array.Empty<string>();
        }

        return csv
            .Split(new[] { ',', ';', ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Contains("@") && x.Length <= 320)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
