using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Dtos;
using ControlPlane.Api.Email;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Linq;

namespace ControlPlane.Api.Controllers;

[ApiController]
[Route("api/v1/agents")]
public sealed class AgentIngestController(
    AppDbContext db,
    SmtpEmailSender email,
    PolicyResolutionService policyResolutionService,
    ILogger<AgentIngestController> logger) : ControllerBase
{
    [HttpGet("/api/v1/health")]
    public IActionResult Health() => Ok(new { status = "ok" });

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

    [HttpPost("configuration/report")]
    public async Task<IActionResult> ConfigurationReport([FromBody] AgentConfigurationReportRequest report, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(report.CustomerId) || string.IsNullOrWhiteSpace(report.HostId))
        {
            return BadRequest("CustomerId e HostId são obrigatórios.");
        }

        var customerId = report.CustomerId.Trim();
        var hostId = report.HostId.Trim();

        var configId = BuildStableConfigurationId(customerId, hostId);
        var existing = await db.AgentConfigurations.FirstOrDefaultAsync(c => c.Id == configId, ct);
        if (existing is null)
        {
            existing = new AgentConfiguration
            {
                Id = configId,
                CustomerId = customerId,
                HostId = hostId,
                PolicyId = null,
                AgentVersion = TrimToMaxLength(report.AgentVersion, 64, defaultValue: "unknown"),
                ServiceStatus = TrimToMaxLength(report.ServiceStatus, 32, defaultValue: "Unknown"),
                TlsMode = TrimToMaxLength(report.TlsMode, 64, defaultValue: "unknown"),
                PrecheckTlsOk = report.PrecheckTlsOk,
                PrecheckDiskOk = report.PrecheckDiskOk,
                PrecheckCredentialOk = report.PrecheckCredentialOk,
                StagingPath = TrimToMaxLength(report.StagingPath, 1024, defaultValue: "N/A"),
                CredentialTargetName = TrimToMaxLength(report.CredentialTargetName, 128, defaultValue: "N/A"),
                UploadMode = TrimToMaxLength(report.UploadMode, 32, defaultValue: "unknown"),
                LastConfigSyncAtUtc = report.TimestampUtc,
                LastPrecheckAtUtc = report.PrecheckAtUtc,
                LastPrecheckMessage = TrimToMaxLengthOrNull(report.PrecheckMessage, 2000),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.AgentConfigurations.Add(existing);
        }
        else
        {
            existing.AgentVersion = TrimToMaxLength(report.AgentVersion, 64, defaultValue: existing.AgentVersion);
            existing.ServiceStatus = TrimToMaxLength(report.ServiceStatus, 32, defaultValue: existing.ServiceStatus);
            existing.TlsMode = TrimToMaxLength(report.TlsMode, 64, defaultValue: existing.TlsMode);
            existing.PrecheckTlsOk = report.PrecheckTlsOk;
            existing.PrecheckDiskOk = report.PrecheckDiskOk;
            existing.PrecheckCredentialOk = report.PrecheckCredentialOk;
            existing.StagingPath = TrimToMaxLength(report.StagingPath, 1024, defaultValue: existing.StagingPath);
            existing.CredentialTargetName = TrimToMaxLength(report.CredentialTargetName, 128, defaultValue: existing.CredentialTargetName);
            existing.UploadMode = TrimToMaxLength(report.UploadMode, 32, defaultValue: existing.UploadMode);
            existing.LastConfigSyncAtUtc = report.TimestampUtc;
            existing.LastPrecheckAtUtc = report.PrecheckAtUtc;
            existing.LastPrecheckMessage = TrimToMaxLengthOrNull(report.PrecheckMessage, 2000);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Configuration report recebido: customer={CustomerId} host={HostId} service={ServiceStatus} tlsOk={TlsOk} diskOk={DiskOk} credOk={CredOk}",
            customerId,
            hostId,
            existing.ServiceStatus,
            existing.PrecheckTlsOk,
            existing.PrecheckDiskOk,
            existing.PrecheckCredentialOk);

        return Ok(new { configurationId = configId });
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

    private static string BuildStableConfigurationId(string customerId, string hostId)
    {
        var input = $"{customerId}\n{hostId}";
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var shortHex = hex.Length > 56 ? hex.Substring(0, 56) : hex;
        return "cfg-" + shortHex;
    }

    private static string TrimToMaxLength(string value, int maxLen, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }

    private static string? TrimToMaxLengthOrNull(string? value, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLen ? trimmed : trimmed.Substring(0, maxLen);
    }
}
