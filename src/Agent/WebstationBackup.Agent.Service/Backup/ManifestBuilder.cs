using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using WebstationBackup.Agent.Service.Core;

namespace WebstationBackup.Agent.Service.Backup;

internal sealed class ManifestBuilder
{
    public ManifestBuildResult Build(string jobId, AgentSettings settings, ProjectRules rules, IReadOnlyList<string> files, string[]? effectiveIncludePaths = null)
    {
        var items = new List<ManifestItem>(files.Count);
        var issues = new List<BackupProcessingIssue>();
        long plannedBytes = 0;

        var hashesEnabled = rules.Defaults.Scan.UseHashes.Enabled && rules.JobDefinition.OkCriteria.Integrity.RequireChecksumWhenAvailable;
        var hashLimit = rules.Defaults.Scan.UseHashes.HashSmallFilesUpToBytes;
        var maxAttempts = Math.Max(1, rules.Defaults.Upload.Retry.MaxAttempts);
        var initialDelay = Math.Max(0, rules.Defaults.Upload.Retry.InitialDelaySeconds);
        var maxDelay = Math.Max(initialDelay, rules.Defaults.Upload.Retry.MaxDelaySeconds);

        foreach (var file in files)
        {
            FileInfo fi;
            try
            {
                fi = new FileInfo(file);
                if (!fi.Exists)
                {
                    issues.Add(CreateIssue("manifest", file, "FILE_MISSING", "Arquivo nao estava mais disponivel durante a montagem do manifest.", isBlocking: true));
                    continue;
                }
            }
            catch (Exception ex)
            {
                issues.Add(CreateIssue("manifest", file, BuildIssueCode(ex), $"Falha ao inspecionar arquivo durante a montagem do manifest. {ex.Message}", isBlocking: true));
                continue;
            }

            plannedBytes += fi.Length;

            string? sha256 = null;
            if (hashesEnabled && fi.Length <= hashLimit)
            {
                var hashResult = ComputeSha256Base64(file, maxAttempts, initialDelay, maxDelay);
                sha256 = hashResult.Value;
                if (hashResult.Issue is not null)
                {
                    issues.Add(hashResult.Issue);
                }
            }

            string rel;
            try
            {
                rel = MakeRelativePathForKey(effectiveIncludePaths ?? settings.IncludePaths, file);
            }
            catch (Exception ex)
            {
                issues.Add(CreateIssue("manifest", file, BuildIssueCode(ex), $"Falha ao calcular caminho relativo para upload. {ex.Message}", isBlocking: true));
                continue;
            }

            items.Add(new ManifestItem
            {
                AbsolutePath = file,
                RelativePath = rel,
                SizeBytes = fi.Length,
                LastWriteTimeUtc = fi.LastWriteTimeUtc,
                Sha256Base64 = sha256
            });
        }

        return new ManifestBuildResult
        {
            Manifest = new BackupManifest
            {
                JobId = jobId,
                CustomerId = settings.CustomerId,
                HostId = settings.HostId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                Items = items,
                PlannedBytes = plannedBytes,
                PlannedItems = items.Count,
                Integrity = new ManifestIntegrityPolicy
                {
                    HashesEnabled = hashesEnabled,
                    HashSmallFilesUpToBytes = hashLimit,
                    Algorithm = "SHA256"
                }
            },
            Issues = issues
        };
    }

    public string SaveToFile(BackupManifest manifest, string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, $"manifest.{manifest.JobId}.json");
        File.WriteAllText(filePath, JsonConvert.SerializeObject(manifest, Formatting.Indented), Encoding.UTF8);
        return filePath;
    }

    private static HashComputationResult ComputeSha256Base64(string path, int maxAttempts, int initialDelaySeconds, int maxDelaySeconds)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(fs);
                    return new HashComputationResult
                    {
                        Value = Convert.ToBase64String(hash),
                        Issue = null
                    };
                }
            }

            catch (Exception ex) when (attempt < maxAttempts && IsRetryableAccessException(ex))
            {
                lastException = ex;
                var delaySeconds = Math.Min(maxDelaySeconds, Math.Max(initialDelaySeconds, initialDelaySeconds * attempt));
                if (delaySeconds > 0)
                {
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
                }
            }
            catch (Exception ex)
            {
                return new HashComputationResult
                {
                    Value = null,
                    Issue = CreateIssue("hash", path, BuildIssueCode(ex), $"Checksum SHA256 indisponivel para o arquivo. O item seguira para upload sem checksum local. {ex.Message}", isBlocking: false)
                };
            }
        }

        return new HashComputationResult
        {
            Value = null,
            Issue = CreateIssue("hash", path, BuildIssueCode(lastException), $"Checksum SHA256 indisponivel apos tentativas de leitura. O item seguira para upload sem checksum local. {lastException?.Message}", isBlocking: false)
        };
    }

    internal static string MakeRelativePathForKey(string[] includePaths, string fullPath)
    {
        foreach (var root in includePaths)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var normalizedRoot = Path.GetFullPath(root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) + Path.DirectorySeparatorChar;
            var normalizedPath = Path.GetFullPath(fullPath);

            if (normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relative = normalizedPath.Substring(normalizedRoot.Length).Replace('\\', '/');
                if (includePaths.Length <= 1) return relative;
                using var sha = SHA256.Create();
                var rootId = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedRoot.ToUpperInvariant()))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
                return "root-" + rootId + "/" + relative;
            }
        }

        throw new InvalidOperationException("Arquivo fora das pastas autorizadas pela politica.");
    }

    private sealed class HashComputationResult
    {
        public string? Value { get; init; }
        public BackupProcessingIssue? Issue { get; init; }
    }

    private static BackupProcessingIssue CreateIssue(string stage, string path, string code, string message, bool isBlocking)
    {
        return new BackupProcessingIssue
        {
            Stage = stage,
            Path = path,
            Code = code,
            Message = message,
            IsBlocking = isBlocking
        };
    }

    private static bool IsRetryableAccessException(Exception ex)
    {
        return ex switch
        {
            IOException ioEx => IsSharingOrLockViolation(ioEx),
            _ => false
        };
    }

    private static string BuildIssueCode(Exception? ex)
    {
        return ex switch
        {
            null => "IO_ERROR",
            PathTooLongException => "PATH_TOO_LONG",
            UnauthorizedAccessException => "ACCESS_DENIED",
            IOException ioEx when IsSharingOrLockViolation(ioEx) => "FILE_IN_USE",
            IOException => "IO_ERROR",
            _ => ex.GetType().Name.ToUpperInvariant()
        };
    }

    private static bool IsSharingOrLockViolation(IOException ex)
    {
        var win32Code = ex.HResult & 0xFFFF;
        return win32Code == 32 || win32Code == 33;
    }
}
