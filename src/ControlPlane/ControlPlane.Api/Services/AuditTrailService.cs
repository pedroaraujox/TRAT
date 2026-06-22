using System.Text.Json;
using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Security;

namespace ControlPlane.Api.Services;

public sealed class AuditTrailService(
    AppDbContext db,
    ILogger<AuditTrailService> logger)
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RecordAsync(
        HttpContext? httpContext,
        string category,
        string action,
        string entityType,
        string? entityId,
        string message,
        string? customerId,
        string? hostId,
        string outcome,
        IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken ct)
    {
        try
        {
            var currentUser = httpContext?.GetCurrentPanelUser();
            var request = httpContext?.Request;
            db.AuditEvents.Add(new AuditEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                Category = NormalizeRequired(category, "general"),
                Action = NormalizeRequired(action, "unknown"),
                Outcome = NormalizeRequired(outcome, "success"),
                EntityType = NormalizeRequired(entityType, "system"),
                EntityId = NormalizeOptional(entityId),
                CustomerId = NormalizeOptional(customerId),
                HostId = NormalizeOptional(hostId),
                ActorUserId = NormalizeOptional(currentUser?.UserId),
                ActorEmail = NormalizeOptional(currentUser?.Email),
                ActorDisplayName = NormalizeOptional(currentUser?.DisplayName),
                Route = NormalizeOptional(request?.Path.Value),
                IpAddress = NormalizeOptional(httpContext?.Connection.RemoteIpAddress?.ToString()),
                UserAgent = NormalizeOptional(request?.Headers.UserAgent.ToString()),
                Message = NormalizeRequired(message, "Evento administrativo registrado."),
                MetadataJson = SerializeMetadata(metadata),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao registrar evento de auditoria {Category}/{Action} para {EntityType}:{EntityId}", category, action, entityType, entityId);
        }
    }

    private static string? SerializeMetadata(IReadOnlyDictionary<string, string?>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var normalized = metadata
            .Where(kvp => !string.IsNullOrWhiteSpace(kvp.Key) && !string.IsNullOrWhiteSpace(kvp.Value))
            .ToDictionary(kvp => kvp.Key.Trim(), kvp => kvp.Value!.Trim(), StringComparer.OrdinalIgnoreCase);
        if (normalized.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(normalized, MetadataJsonOptions);
    }

    private static string NormalizeRequired(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
