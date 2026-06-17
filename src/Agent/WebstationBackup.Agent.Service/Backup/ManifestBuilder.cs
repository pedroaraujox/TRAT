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
    public BackupManifest Build(string jobId, AgentSettings settings, ProjectRules rules, IReadOnlyList<string> files)
    {
        var items = new List<ManifestItem>(files.Count);
        long plannedBytes = 0;

        var hashesEnabled = rules.Defaults.Scan.UseHashes.Enabled && rules.JobDefinition.OkCriteria.Integrity.RequireChecksumWhenAvailable;
        var hashLimit = rules.Defaults.Scan.UseHashes.HashSmallFilesUpToBytes;

        foreach (var file in files)
        {
            FileInfo fi;
            try
            {
                fi = new FileInfo(file);
                if (!fi.Exists)
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            plannedBytes += fi.Length;

            string? sha256 = null;
            if (hashesEnabled && fi.Length <= hashLimit)
            {
                sha256 = ComputeSha256Base64(file);
            }

            var rel = MakeRelativePathForKey(settings.IncludePaths, file);
            items.Add(new ManifestItem
            {
                AbsolutePath = file,
                RelativePath = rel,
                SizeBytes = fi.Length,
                LastWriteTimeUtc = fi.LastWriteTimeUtc,
                Sha256Base64 = sha256
            });
        }

        return new BackupManifest
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
        };
    }

    public string SaveToFile(BackupManifest manifest, string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);
        var filePath = Path.Combine(directoryPath, $"manifest.{manifest.JobId}.json");
        File.WriteAllText(filePath, JsonConvert.SerializeObject(manifest, Formatting.Indented), Encoding.UTF8);
        return filePath;
    }

    private static string? ComputeSha256Base64(string path)
    {
        try
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(fs);
                return Convert.ToBase64String(hash);
            }
        }
        catch
        {
            return null;
        }
    }

    private static string MakeRelativePathForKey(string[] includePaths, string fullPath)
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
                return normalizedPath.Substring(normalizedRoot.Length).Replace('\\', '/');
            }
        }

        return Path.GetFileName(fullPath);
    }
}

