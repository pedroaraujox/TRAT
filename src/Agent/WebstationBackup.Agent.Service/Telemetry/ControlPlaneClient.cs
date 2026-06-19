using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using WebstationBackup.Agent.Service.Core;
using WebstationBackup.Agent.Service.Logging;

namespace WebstationBackup.Agent.Service.Telemetry;

internal sealed class RemoteRunRequestResponse
{
    public string RunRequestId { get; set; } = string.Empty;
    public string TriggerType { get; set; } = string.Empty;
    public DateTimeOffset RequestedAtUtc { get; set; }
}

internal sealed class ControlPlaneClient
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public ControlPlaneClient(string baseUrl, string agentToken, ILogger logger)
    {
        _logger = logger;
        _http = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.Add("X-Agent-Token", agentToken);
        _http.Timeout = TimeSpan.FromSeconds(20);
    }

    public Task SendHeartbeatAsync(object payload, CancellationToken ct) => PostJsonAsync("api/v1/agents/heartbeat", payload, ct);
    public Task StartJobAsync(object payload, CancellationToken ct) => PostJsonAsync("api/v1/agents/jobs/start", payload, ct);
    public Task ReportProgressAsync(object payload, CancellationToken ct) => PostJsonAsync("api/v1/agents/jobs/progress", payload, ct);
    public Task ReportFinalAsync(object payload, CancellationToken ct) => PostJsonAsync("api/v1/agents/jobs/final", payload, ct);
    public Task ReportConfigurationAsync(object payload, CancellationToken ct) => PostJsonAsync("api/v1/agents/configuration/report", payload, ct);
    public async Task<RemoteRunRequestResponse?> TryGetPendingRunRequestAsync(string customerId, string hostId, CancellationToken ct)
    {
        var path = "api/v1/agents/run-request/next?customerId=" + Uri.EscapeDataString(customerId) + "&hostId=" + Uri.EscapeDataString(hostId);

        try
        {
            var resp = await _http.GetAsync(path, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                return null;
            }

            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.Warn("ControlPlane run request polling failed", new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["statusCode"] = (int)resp.StatusCode,
                    ["body"] = body
                });
                return null;
            }

            return JsonConvert.DeserializeObject<RemoteRunRequestResponse>(body);
        }
        catch (Exception ex)
        {
            _logger.Warn("ControlPlane run request polling raised exception", new System.Collections.Generic.Dictionary<string, object?>
            {
                ["path"] = path,
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message
            });
            return null;
        }
    }

    public async Task<RemoteEffectivePolicyResponse?> TryGetEffectivePolicyAsync(string customerId, string hostId, CancellationToken ct)
    {
        var path = "api/v1/agents/effective-policy?customerId=" + Uri.EscapeDataString(customerId) + "&hostId=" + Uri.EscapeDataString(hostId);

        try
        {
            var resp = await _http.GetAsync(path, ct);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.Warn("ControlPlane effective policy request failed", new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["statusCode"] = (int)resp.StatusCode,
                    ["body"] = body
                });
                return null;
            }

            var parsed = JsonConvert.DeserializeObject<RemoteEffectivePolicyResponse>(body);
            if (parsed is null)
            {
                _logger.Warn("ControlPlane effective policy response could not be deserialized", new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["path"] = path
                });
            }

            return parsed;
        }
        catch (Exception ex)
        {
            _logger.Warn("ControlPlane effective policy request raised exception", new System.Collections.Generic.Dictionary<string, object?>
            {
                ["path"] = path,
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message
            });
            return null;
        }
    }

    private async Task PostJsonAsync(string path, object payload, CancellationToken ct)
    {
        var json = JsonConvert.SerializeObject(payload);
        using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
        {
            var resp = await _http.PostAsync(path, content, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync();
                _logger.Warn("ControlPlane request failed", new System.Collections.Generic.Dictionary<string, object?>
                {
                    ["path"] = path,
                    ["statusCode"] = (int)resp.StatusCode,
                    ["body"] = body
                });
                resp.EnsureSuccessStatusCode();
            }
        }
    }
}
