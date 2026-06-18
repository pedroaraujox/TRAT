using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebstationBackup.Agent.Installer;

internal sealed record ControlPlanePingResult(bool Success, string Message, int? StatusCode);
internal sealed record ControlPlaneEnrollResult(bool Success, string Message, int? StatusCode, string? CustomerId, string? HostId, string? ExpectedAwsAccountId);

internal static class ControlPlanePingTester
{
    public static async Task<ControlPlanePingResult> PingAsync(string baseUrl, string agentToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return new ControlPlanePingResult(false, "ControlPlaneBaseUrl invalida.", null);
        }

        var target = new Uri(uri.ToString().TrimEnd('/') + "/api/v1/agents/ping");
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        using var req = new HttpRequestMessage(HttpMethod.Get, target);
        if (!string.IsNullOrWhiteSpace(agentToken))
        {
            req.Headers.TryAddWithoutValidation("X-Agent-Token", agentToken.Trim());
        }

        try
        {
            using var resp = await client.SendAsync(req, ct);
            var code = (int)resp.StatusCode;
            if (resp.IsSuccessStatusCode)
            {
                return new ControlPlanePingResult(true, "Ping OK (token aceito).", code);
            }

            return new ControlPlanePingResult(false, $"Ping falhou: HTTP {code}.", code);
        }
        catch (TaskCanceledException)
        {
            return new ControlPlanePingResult(false, "Ping expirou (timeout).", null);
        }
        catch (Exception ex)
        {
            return new ControlPlanePingResult(false, $"Ping falhou: {ex.Message}", null);
        }
    }

    public static async Task<ControlPlaneEnrollResult> EnrollAsync(string baseUrl, string agentToken, string hostId, string hostname, string osVersion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return new ControlPlaneEnrollResult(false, "ControlPlaneBaseUrl invalida.", null, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(agentToken))
        {
            return new ControlPlaneEnrollResult(false, "Token do Agent é obrigatório.", null, null, null, null);
        }

        if (string.IsNullOrWhiteSpace(hostId) || string.IsNullOrWhiteSpace(hostname) || string.IsNullOrWhiteSpace(osVersion))
        {
            return new ControlPlaneEnrollResult(false, "HostId/Hostname/OsVersion são obrigatórios.", null, null, null, null);
        }

        var target = new Uri(uri.ToString().TrimEnd('/') + "/api/v1/agents/enroll");
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        var payload = JsonSerializer.Serialize(new
        {
            hostId = hostId.Trim(),
            hostname = hostname.Trim(),
            osVersion = osVersion.Trim()
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        req.Headers.TryAddWithoutValidation("X-Agent-Token", agentToken.Trim());

        try
        {
            using var resp = await client.SendAsync(req, ct);
            var code = (int)resp.StatusCode;
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                return new ControlPlaneEnrollResult(false, $"Enroll falhou: HTTP {code}.", code, null, null, null);
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                var customerId = root.TryGetProperty("customerId", out var c) ? c.GetString() : null;
                var enrolledHostId = root.TryGetProperty("hostId", out var h) ? h.GetString() : null;
                var expectedAws = root.TryGetProperty("expectedAwsAccountId", out var a) ? a.GetString() : null;
                if (string.IsNullOrWhiteSpace(customerId) || string.IsNullOrWhiteSpace(enrolledHostId))
                {
                    return new ControlPlaneEnrollResult(false, "Enroll OK, mas resposta inválida do ControlPlane.", code, null, null, null);
                }

                return new ControlPlaneEnrollResult(true, "Enroll OK (token aceito).", code, customerId, enrolledHostId, expectedAws);
            }
            catch
            {
                return new ControlPlaneEnrollResult(false, "Enroll OK, mas nao foi possivel ler a resposta do ControlPlane.", code, null, null, null);
            }
        }
        catch (TaskCanceledException)
        {
            return new ControlPlaneEnrollResult(false, "Enroll expirou (timeout).", null, null, null, null);
        }
        catch (Exception ex)
        {
            return new ControlPlaneEnrollResult(false, $"Enroll falhou: {ex.Message}", null, null, null, null);
        }
    }
}
