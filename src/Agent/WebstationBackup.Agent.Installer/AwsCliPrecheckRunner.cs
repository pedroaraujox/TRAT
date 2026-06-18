using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebstationBackup.Agent.Installer;

internal sealed record AwsCliPrecheckResult(
    bool Success,
    string Message,
    string? AwsAccountId,
    string? BucketRegion,
    string RawOutput);

internal static class AwsCliPrecheckRunner
{
    public static async Task<AwsCliPrecheckResult> RunIdentityAsync(CancellationToken ct)
    {
        var sb = new StringBuilder();

        var sts = await RunAwsAsync("sts get-caller-identity --output json --no-cli-pager", sb, ct);
        if (!sts.Success)
        {
            return new AwsCliPrecheckResult(false, "Falha no STS get-caller-identity via AWS CLI.", null, null, sb.ToString());
        }

        var accountId = TryReadJsonString(sts.StdOut, "Account");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new AwsCliPrecheckResult(false, "AWS CLI respondeu STS, mas sem AccountId.", null, null, sb.ToString());
        }

        return new AwsCliPrecheckResult(true, "AWS CLI OK (identidade lida em modo somente leitura).", accountId.Trim(), null, sb.ToString());
    }

    public static async Task<AwsCliPrecheckResult> RunAsync(string region, string bucket, string prefix, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return new AwsCliPrecheckResult(false, "AwsRegion é obrigatório para validar bucket/prefixo.", null, null, string.Empty);
        }

        var sb = new StringBuilder();

        var sts = await RunAwsAsync($"sts get-caller-identity --output json --no-cli-pager", sb, ct);
        if (!sts.Success)
        {
            return new AwsCliPrecheckResult(false, "Falha no STS get-caller-identity via AWS CLI.", null, null, sb.ToString());
        }

        var accountId = TryReadJsonString(sts.StdOut, "Account");
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new AwsCliPrecheckResult(false, "AWS CLI respondeu STS, mas sem AccountId.", null, null, sb.ToString());
        }

        if (string.IsNullOrWhiteSpace(bucket))
        {
            return new AwsCliPrecheckResult(true, "AWS CLI OK (STS OK). Bucket/prefixo nao informados; checks de S3 foram ignorados.", accountId.Trim(), null, sb.ToString());
        }

        var loc = await RunAwsAsync($"s3api get-bucket-location --bucket \"{bucket.Trim()}\" --output json --region \"{region.Trim()}\" --no-cli-pager", sb, ct);
        if (!loc.Success)
        {
            return new AwsCliPrecheckResult(false, "Falha no S3 get-bucket-location via AWS CLI.", accountId, null, sb.ToString());
        }

        var regionRaw = TryReadJsonString(loc.StdOut, "LocationConstraint");
        var bucketRegion = string.IsNullOrWhiteSpace(regionRaw) ? "us-east-1" : regionRaw.Trim();

        if (!string.IsNullOrWhiteSpace(prefix))
        {
            var normalizedPrefix = prefix.Trim().Replace('\\', '/').TrimStart('/');
            await RunAwsAsync($"s3api list-objects-v2 --bucket \"{bucket.Trim()}\" --prefix \"{normalizedPrefix}\" --max-items 1 --output json --region \"{region.Trim()}\" --no-cli-pager", sb, ct);
        }

        return new AwsCliPrecheckResult(true, "AWS CLI precheck OK (somente leitura).", accountId, bucketRegion, sb.ToString());
    }

    private static async Task<(bool Success, string StdOut)> RunAwsAsync(string arguments, StringBuilder log, CancellationToken ct)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "aws",
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            log.AppendLine($"> aws {arguments}");
            if (!process.Start())
            {
                log.AppendLine("Falha ao iniciar aws.exe.");
                return (false, string.Empty);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (!string.IsNullOrWhiteSpace(stdout))
            {
                log.AppendLine(stdout.Trim());
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                log.AppendLine(stderr.Trim());
            }

            return (process.ExitCode == 0, stdout);
        }
        catch (Win32Exception)
        {
            log.AppendLine("aws.exe nao encontrado no PATH.");
            return (false, string.Empty);
        }
        catch (TaskCanceledException)
        {
            log.AppendLine("AWS CLI precheck expirou (timeout).");
            return (false, string.Empty);
        }
        catch (Exception ex)
        {
            log.AppendLine("Falha ao executar AWS CLI: " + ex.Message);
            return (false, string.Empty);
        }
    }

    private static string? TryReadJsonString(string json, string propertyName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
        catch
        {
            return null;
        }
    }
}
