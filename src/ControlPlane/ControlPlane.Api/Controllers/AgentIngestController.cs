using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Dtos;
using ControlPlane.Api.Email;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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
    OperationalAlertService operationalAlertService,
    ILogger<AgentIngestController> logger) : ControllerBase
{
    public sealed record AgentEnrollRequest(string HostId, string Hostname, string OsVersion);
    public sealed record AgentEnrollResponse(string CustomerId, string HostId, string ExpectedAwsAccountId);
    public sealed record PendingRunRequestResponse(string RunRequestId, string TriggerType, DateTimeOffset RequestedAtUtc);

    public sealed record AgentBootstrapPathsRequest(string HostId, string[] IncludePaths, string[] ExcludePaths);

    [HttpGet("/api/v1/health")]
    public IActionResult Health() => Ok(new { status = "ok" });

    [HttpGet("ping")]
    public IActionResult Ping() => Ok(new { ok = true, tsUtc = DateTimeOffset.UtcNow });

    [HttpPost("enroll")]
    public async Task<IActionResult> Enroll([FromBody] AgentEnrollRequest request, CancellationToken ct)
    {
        var customerId = GetAuthenticatedAgentCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        if (request is null ||
            string.IsNullOrWhiteSpace(request.HostId) ||
            string.IsNullOrWhiteSpace(request.Hostname) ||
            string.IsNullOrWhiteSpace(request.OsVersion))
        {
            return BadRequest("HostId, Hostname e OsVersion são obrigatórios.");
        }

        var normalizedHostId = request.HostId.Trim();
        if (normalizedHostId.Length > 64)
        {
            return BadRequest("HostId inválido (muito longo).");
        }

        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (customer is null)
        {
            return Unauthorized();
        }

        var effectiveHostId = normalizedHostId;
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.CustomerId == customerId && h.Id == effectiveHostId, ct);
        if (host is null)
        {
            var collidesWithAnotherCustomer = await db.Hosts.AsNoTracking().AnyAsync(
                h => h.Id == normalizedHostId && h.CustomerId != customerId,
                ct);
            if (collidesWithAnotherCustomer)
            {
                effectiveHostId = BuildScopedHostId(customerId, normalizedHostId);
                logger.LogWarning(
                    "Enroll detectou colisao global de HostId. customer={CustomerId} requestedHostId={RequestedHostId} scopedHostId={ScopedHostId}",
                    customerId,
                    normalizedHostId,
                    effectiveHostId);
            }

            host = await db.Hosts.FirstOrDefaultAsync(h => h.CustomerId == customerId && h.Id == effectiveHostId, ct);
            if (host is null)
            {
                host = new ControlPlane.Api.Domain.Host
                {
                    Id = effectiveHostId,
                    CustomerId = customerId,
                    Hostname = request.Hostname.Trim(),
                    OsVersion = request.OsVersion.Trim(),
                    FirstSeenAtUtc = DateTimeOffset.UtcNow,
                    LastHeartbeatAtUtc = null
                };
                db.Hosts.Add(host);
            }
        }
        else
        {
            host.Hostname = request.Hostname.Trim();
            host.OsVersion = request.OsVersion.Trim();
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(
                ex,
                "Enroll falhou ao persistir host. customer={CustomerId} requestedHostId={RequestedHostId} effectiveHostId={EffectiveHostId}",
                customerId,
                normalizedHostId,
                effectiveHostId);

            return Problem(
                detail: "Falha ao registrar o host no ControlPlane.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        return Ok(new AgentEnrollResponse(customerId, effectiveHostId, customer.AwsAccountId));
    }

    [HttpPost("bootstrap/paths")]
    public async Task<IActionResult> BootstrapPaths([FromBody] AgentBootstrapPathsRequest request, CancellationToken ct)
    {
        var customerId = GetAuthenticatedAgentCustomerId();
        if (customerId is null)
        {
            return Unauthorized();
        }

        if (request is null || string.IsNullOrWhiteSpace(request.HostId))
        {
            return BadRequest("HostId é obrigatório.");
        }

        var normalizedHostId = request.HostId.Trim();
        var host = await db.Hosts.FirstOrDefaultAsync(h => h.CustomerId == customerId && h.Id == normalizedHostId, ct);
        if (host is null)
        {
            return NotFound("Host não encontrado para este cliente.");
        }

        var include = NormalizeCsvPathList(request.IncludePaths);
        var exclude = NormalizeCsvPathList(request.ExcludePaths);

        host.BootstrapIncludePathsCsv = include;
        host.BootstrapExcludePathsCsv = exclude;
        await db.SaveChangesAsync(ct);

        return Ok(new { ok = true });
    }

    [HttpGet("effective-policy")]
    public async Task<IActionResult> EffectivePolicy([FromQuery] string customerId, [FromQuery] string hostId, CancellationToken ct)
    {
        var authCustomerId = GetAuthenticatedAgentCustomerId();
        if (authCustomerId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(hostId))
        {
            if (string.IsNullOrWhiteSpace(hostId))
            {
                return BadRequest("HostId é obrigatório.");
            }

            customerId = authCustomerId;
        }

        if (!string.Equals(customerId.Trim(), authCustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var response = await policyResolutionService.ResolveEffectivePolicyAsync(customerId, hostId, ct);
        return Ok(response);
    }

    [HttpGet("run-request/next")]
    public async Task<IActionResult> NextRunRequest([FromQuery] string customerId, [FromQuery] string hostId, CancellationToken ct)
    {
        var authCustomerId = GetAuthenticatedAgentCustomerId();
        if (authCustomerId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(hostId))
        {
            return BadRequest("CustomerId e HostId sao obrigatorios.");
        }

        var normalizedCustomerId = customerId.Trim();
        var normalizedHostId = hostId.Trim();
        if (!string.Equals(normalizedCustomerId, authCustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var reclaimClaimedBeforeUtc = nowUtc.AddMinutes(-10);
        var request = (await db.AgentRunRequests
            .Where(r => r.CustomerId == normalizedCustomerId &&
                        r.HostId == normalizedHostId &&
                        (r.State == "QUEUED" || r.State == "CLAIMED"))
            .ToListAsync(ct))
            .Where(r => r.State == "QUEUED" ||
                        (r.State == "CLAIMED" && r.ClaimedAtUtc != null && r.ClaimedAtUtc < reclaimClaimedBeforeUtc))
            .OrderBy(r => r.RequestedAtUtc)
            .FirstOrDefault();
        if (request is null)
        {
            return NoContent();
        }

        request.State = "CLAIMED";
        request.ClaimedAtUtc = nowUtc;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Run request claimed: customer={CustomerId} host={HostId} runRequestId={RunRequestId}",
            normalizedCustomerId,
            normalizedHostId,
            request.Id);

        return Ok(new PendingRunRequestResponse(request.Id, request.TriggerType, request.RequestedAtUtc));
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] AgentHeartbeatRequest request, CancellationToken ct)
    {
        var authCustomerId = GetAuthenticatedAgentCustomerId();
        if (authCustomerId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.HostId))
        {
            return BadRequest("CustomerId e HostId são obrigatórios.");
        }

        if (!string.Equals(request.CustomerId.Trim(), authCustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var normalizedCustomerId = request.CustomerId.Trim();
        var normalizedHostId = request.HostId.Trim();

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.CustomerId == normalizedCustomerId && h.Id == normalizedHostId, ct);
        if (host is null)
        {
            var collidesWithAnotherCustomer = await db.Hosts.AsNoTracking().AnyAsync(
                h => h.Id == normalizedHostId && h.CustomerId != normalizedCustomerId,
                ct);
            if (collidesWithAnotherCustomer)
            {
                logger.LogWarning(
                    "Heartbeat rejeitado por colisao global de HostId. customer={CustomerId} host={HostId}",
                    normalizedCustomerId,
                    normalizedHostId);
                return Conflict("HostId já existe para outro cliente. Reinstale/re-enrole o Agent para obter um HostId escopado por cliente.");
            }

            host = new ControlPlane.Api.Domain.Host
            {
                Id = normalizedHostId,
                CustomerId = normalizedCustomerId,
                Hostname = request.Hostname.Trim(),
                OsVersion = request.OsVersion.Trim(),
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
        await operationalAlertService.ReconcileHostAsync(normalizedCustomerId, normalizedHostId, ct);
        logger.LogInformation("Heartbeat recebido: customer={CustomerId} host={HostId} os={OsVersion}", normalizedCustomerId, normalizedHostId, request.OsVersion);
        return Ok();
    }

    [HttpPost("jobs/start")]
    public async Task<IActionResult> JobStart([FromBody] JobStartRequest request, CancellationToken ct)
    {
        var authCustomerId = GetAuthenticatedAgentCustomerId();
        if (authCustomerId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CustomerId) || string.IsNullOrWhiteSpace(request.HostId) || string.IsNullOrWhiteSpace(request.JobId))
        {
            return BadRequest("CustomerId, HostId e JobId são obrigatórios.");
        }

        if (!string.Equals(request.CustomerId.Trim(), authCustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
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

        if (!string.IsNullOrWhiteSpace(request.RunRequestId))
        {
            var runRequest = await db.AgentRunRequests.FirstOrDefaultAsync(
                r => r.Id == request.RunRequestId && r.CustomerId == request.CustomerId && r.HostId == request.HostId,
                ct);
            if (runRequest is not null)
            {
                runRequest.State = "RUNNING";
                runRequest.JobId = request.JobId;
                runRequest.ClaimedAtUtc ??= request.StartedAtUtc;
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Job iniciado: customer={CustomerId} host={HostId} job={JobId}", request.CustomerId, request.HostId, request.JobId);
        return Ok();
    }

    [HttpPost("jobs/progress")]
    public async Task<IActionResult> JobProgress([FromBody] JobProgressReport report, CancellationToken ct)
    {
        var authCustomerId = GetAuthenticatedAgentCustomerId();
        if (authCustomerId is null)
        {
            return Unauthorized();
        }

        if (!string.Equals(report.CustomerId?.Trim(), authCustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

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
        var authCustomerId = GetAuthenticatedAgentCustomerId();
        if (authCustomerId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(report.CustomerId) || string.IsNullOrWhiteSpace(report.HostId) || string.IsNullOrWhiteSpace(report.JobId))
        {
            return BadRequest("CustomerId, HostId e JobId são obrigatórios.");
        }

        var customerId = report.CustomerId.Trim();
        var hostId = report.HostId.Trim();
        var jobId = report.JobId.Trim();

        if (!string.Equals(customerId, authCustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        var job = await db.Jobs.FirstOrDefaultAsync(j => j.CustomerId == customerId && j.HostId == hostId && j.Id == jobId, ct);
        if (job is null)
        {
            return NotFound("Job não encontrado.");
        }

        var isReplay = job.FinishedAtUtc.HasValue;
        job.State = report.FinalState;
        job.FinishedAtUtc = report.FinishedAtUtc;
        job.PlannedBytes = report.PlannedBytes;
        job.PlannedItems = report.PlannedItems;
        job.UploadedBytes = report.UploadedBytes;
        job.UploadedItems = report.UploadedItems;
        job.FailureCode = report.FailureCode;
        job.FailureMessage = report.FailureMessage;

        if (!string.IsNullOrWhiteSpace(report.RunRequestId))
        {
            var runRequest = await db.AgentRunRequests.FirstOrDefaultAsync(
                r => r.Id == report.RunRequestId && r.CustomerId == customerId && r.HostId == hostId,
                ct);
            if (runRequest is not null)
            {
                runRequest.JobId = jobId;
                runRequest.CompletedAtUtc = report.FinishedAtUtc;
                runRequest.FailureMessage = TrimToMaxLengthOrNull(report.FailureMessage, 2000);
                runRequest.State = string.Equals(report.FinalState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                    ? "COMPLETED"
                    : "FAILED";
            }
        }

        var existingArtifactKeys = await db.Artifacts.AsNoTracking()
            .Where(a => a.JobId == job.Id)
            .Select(a => a.Type + "\n" + a.Location)
            .ToListAsync(ct);
        var existingArtifactSet = new HashSet<string>(existingArtifactKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var a in report.Artifacts)
        {
            var artifactKey = a.Type + "\n" + a.Location;
            if (!existingArtifactSet.Add(artifactKey))
            {
                continue;
            }

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
        if (!isReplay)
        {
            await operationalAlertService.ObserveJobFinalStateAsync(job, ct);
        }
        await operationalAlertService.ReconcileHostAsync(customerId, hostId, ct);

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        if (!isReplay && customer is not null)
        {
            var recipients = ParseEmails(customer.NotificationEmailsCsv);
            var subject = $"Backup {report.FinalState} - Cliente {customer.Name} - Host {hostId}";
            var body =
                $"JobId: {jobId}\n" +
                $"Cliente: {customer.Name} ({customer.Id})\n" +
                $"Host: {hostId}\n" +
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

        logger.LogInformation(
            "Job finalizado: customer={CustomerId} host={HostId} job={JobId} state={State} replay={Replay}",
            customerId,
            hostId,
            jobId,
            report.FinalState,
            isReplay);
        return Ok();
    }

    [HttpPost("configuration/report")]
    public async Task<IActionResult> ConfigurationReport([FromBody] AgentConfigurationReportRequest report, CancellationToken ct)
    {
        var authCustomerId = GetAuthenticatedAgentCustomerId();
        if (authCustomerId is null)
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(report.CustomerId) || string.IsNullOrWhiteSpace(report.HostId))
        {
            return BadRequest("CustomerId e HostId são obrigatórios.");
        }

        if (!string.Equals(report.CustomerId.Trim(), authCustomerId, StringComparison.OrdinalIgnoreCase))
        {
            return Forbid();
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
                EffectivePolicyId = TrimToMaxLengthOrNull(report.EffectivePolicyId, 64),
                EffectivePolicyName = TrimToMaxLengthOrNull(report.EffectivePolicyName, 128),
                EffectivePolicyKind = TrimToMaxLengthOrNull(report.EffectivePolicyKind, 32),
                EffectivePolicySource = TrimToMaxLengthOrNull(report.EffectivePolicySource, 64),
                EffectivePolicyLastChangedAtUtc = report.EffectivePolicyLastChangedAtUtc,
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
                AwsAccountId = TrimToMaxLengthOrNull(report.AwsAccountId, 32),
                BucketDiscoveryOk = report.BucketDiscoveryOk,
                BucketDiscoveryMessage = TrimToMaxLengthOrNull(report.BucketDiscoveryMessage, 1000),
                AvailableBucketsCsv = NormalizeBucketList(report.AvailableBuckets),
                AvailableBucketRegionsJson = NormalizeBucketRegions(report.AvailableBucketRegions),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.AgentConfigurations.Add(existing);
        }
        else
        {
            existing.EffectivePolicyId = TrimToMaxLengthOrNull(report.EffectivePolicyId, 64);
            existing.EffectivePolicyName = TrimToMaxLengthOrNull(report.EffectivePolicyName, 128);
            existing.EffectivePolicyKind = TrimToMaxLengthOrNull(report.EffectivePolicyKind, 32);
            existing.EffectivePolicySource = TrimToMaxLengthOrNull(report.EffectivePolicySource, 64);
            existing.EffectivePolicyLastChangedAtUtc = report.EffectivePolicyLastChangedAtUtc;
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
            existing.AwsAccountId = TrimToMaxLengthOrNull(report.AwsAccountId, 32);
            existing.BucketDiscoveryOk = report.BucketDiscoveryOk;
            existing.BucketDiscoveryMessage = TrimToMaxLengthOrNull(report.BucketDiscoveryMessage, 1000);
            existing.AvailableBucketsCsv = NormalizeBucketList(report.AvailableBuckets);
            existing.AvailableBucketRegionsJson = NormalizeBucketRegions(report.AvailableBucketRegions);
        }

        await db.SaveChangesAsync(ct);
        await operationalAlertService.ReconcileHostAsync(customerId, hostId, ct);
        logger.LogInformation(
            "Configuration report recebido: customer={CustomerId} host={HostId} service={ServiceStatus} effectivePolicyId={EffectivePolicyId} effectivePolicySource={EffectivePolicySource} tlsOk={TlsOk} diskOk={DiskOk} credOk={CredOk}",
            customerId,
            hostId,
            existing.ServiceStatus,
            existing.EffectivePolicyId,
            existing.EffectivePolicySource,
            existing.PrecheckTlsOk,
            existing.PrecheckDiskOk,
            existing.PrecheckCredentialOk);

        return Ok(new { configurationId = configId });
    }

    private static string? NormalizeBucketList(IReadOnlyList<string>? buckets)
    {
        if (buckets is null || buckets.Count == 0)
        {
            return null;
        }

        var normalized = buckets
            .Where(bucket => !string.IsNullOrWhiteSpace(bucket))
            .Select(bucket => bucket.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(bucket => bucket, StringComparer.OrdinalIgnoreCase);
        return TrimToMaxLengthOrNull(string.Join(',', normalized), 8000);
    }

    private static string? NormalizeBucketRegions(IReadOnlyDictionary<string, string>? bucketRegions)
    {
        if (bucketRegions is null || bucketRegions.Count == 0)
        {
            return null;
        }

        var normalized = bucketRegions
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
            .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                item => item.Key.Trim(),
                item => item.Value.Trim(),
                StringComparer.OrdinalIgnoreCase);
        return TrimToMaxLengthOrNull(JsonSerializer.Serialize(normalized), 16000);
    }

    private string? GetAuthenticatedAgentCustomerId()
    {
        return HttpContext.Items.TryGetValue("AgentCustomerId", out var raw) ? raw as string : null;
    }

    private static string? NormalizeCsvPathList(string[]? values)
    {
        if (values is null || values.Length == 0)
        {
            return null;
        }

        var normalized = values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
        {
            return null;
        }

        var joined = string.Join(";", normalized);
        return joined.Length > 4000 ? joined.Substring(0, 4000) : joined;
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

    private static string BuildScopedHostId(string customerId, string requestedHostId)
    {
        var c = customerId.Trim();
        var h = requestedHostId.Trim();

        var suffix = ShortStableSuffix(c);
        const string separator = "--";
        var reserved = separator.Length + suffix.Length;

        var maxHostPartLen = 64 - reserved;
        if (maxHostPartLen <= 0)
        {
            throw new InvalidOperationException("HostId escopado inválido por restrição de tamanho.");
        }

        var hostPart = h.Length <= maxHostPartLen ? h : h.Substring(0, maxHostPartLen);
        return hostPart + separator + suffix;
    }

    private static string ShortStableSuffix(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return hex.Substring(0, 10);
    }
}
