using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WebstationBackup.Agent.Service.Backup;

internal sealed class FileScanner
{
    private static readonly HashSet<string> DefaultIgnoredFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini",
        "thumbs.db",
        "ehthumbs.db"
    };

    private static readonly HashSet<string> DefaultIgnoredDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information",
        "$RECYCLE.BIN"
    };

    public ConfiguredPathValidationResult ValidateConfiguredPaths(IEnumerable<string> includePaths, IEnumerable<string> excludePaths)
    {
        var issues = new List<BackupProcessingIssue>();
        var includes = NormalizeConfiguredPaths(includePaths, "include", issues);
        var excludes = NormalizeConfiguredPaths(excludePaths, "exclude", issues);

        return new ConfiguredPathValidationResult
        {
            IncludePaths = includes,
            ExcludePaths = excludes,
            Issues = issues
        };
    }

    public FileScanResult ScanFiles(IEnumerable<string> includePaths, IEnumerable<string> excludePaths)
    {
        var validation = ValidateConfiguredPaths(includePaths, excludePaths);
        var includes = validation.IncludePaths.ToArray();
        var excludes = validation.ExcludePaths.ToArray();

        var results = new List<string>(capacity: 4096);
        foreach (var root in includes)
        {
            if (File.Exists(root))
            {
                if (!IsExcluded(root, excludes) && !IsIgnoredMetadataFile(root))
                {
                    results.Add(root);
                }
                continue;
            }

            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in EnumerateFilesSafe(root, validation.Issues))
            {
                if (IsExcluded(file, excludes) || IsIgnoredMetadataFile(file))
                {
                    continue;
                }
                results.Add(file);
            }
        }

        return new FileScanResult
        {
            Files = results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            Issues = validation.Issues
        };
    }

    private static IReadOnlyList<string> NormalizeConfiguredPaths(IEnumerable<string> paths, string scope, ICollection<BackupProcessingIssue> issues)
    {
        var normalized = new List<string>();

        foreach (var rawPath in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            string candidate;
            try
            {
                candidate = NormalizePath(rawPath);
            }
            catch (Exception ex)
            {
                issues.Add(CreateIssue("path_validation", rawPath ?? string.Empty, BuildIssueCode(ex), $"Falha ao normalizar path configurado de {scope}. {ex.Message}", isBlocking: scope == "include"));
                continue;
            }

            var existsAsFile = false;
            var existsAsDirectory = false;
            try
            {
                existsAsFile = File.Exists(candidate);
                existsAsDirectory = Directory.Exists(candidate);
            }
            catch (Exception ex)
            {
                issues.Add(CreateIssue("path_validation", candidate, BuildIssueCode(ex), $"Falha ao validar path configurado de {scope}. {ex.Message}", isBlocking: scope == "include"));
                continue;
            }

            if (!existsAsFile && !existsAsDirectory)
            {
                issues.Add(CreateIssue("path_validation", candidate, scope == "include" ? "INCLUDE_PATH_MISSING" : "EXCLUDE_PATH_MISSING", $"Path configurado de {scope} nao foi encontrado no host.", isBlocking: scope == "include"));
                continue;
            }

            normalized.Add(candidate);
        }

        return normalized.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, IReadOnlyList<BackupProcessingIssue> issues)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch (Exception ex)
            {
                AddIssue(issues, CreateIssue("scan", current, BuildIssueCode(ex), $"Falha ao enumerar arquivos em '{current}'. {ex.Message}", isBlocking: true));
                continue;
            }

            foreach (var f in files)
            {
                yield return f;
            }

            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(current);
            }
            catch (Exception ex)
            {
                AddIssue(issues, CreateIssue("scan", current, BuildIssueCode(ex), $"Falha ao enumerar diretorios em '{current}'. {ex.Message}", isBlocking: true));
                continue;
            }

            foreach (var d in dirs)
            {
                if (IsIgnoredDirectory(d))
                {
                    continue;
                }

                stack.Push(d);
            }
        }
    }

    private static void AddIssue(IReadOnlyList<BackupProcessingIssue> issues, BackupProcessingIssue issue)
    {
        if (issues is List<BackupProcessingIssue> mutableIssues)
        {
            mutableIssues.Add(issue);
        }
    }

    private static bool IsExcluded(string path, string[] excludes)
    {
        foreach (var ex in excludes)
        {
            if (path.StartsWith(ex, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsIgnoredMetadataFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return !string.IsNullOrWhiteSpace(fileName) && DefaultIgnoredFileNames.Contains(fileName);
    }

    private static bool IsIgnoredDirectory(string path)
    {
        try
        {
            var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (!string.IsNullOrWhiteSpace(name) && DefaultIgnoredDirectoryNames.Contains(name))
            {
                return true;
            }

            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return true;
        }
    }

    private static string NormalizePath(string p) => Path.GetFullPath(p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

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

    private static string BuildIssueCode(Exception ex)
    {
        return ex switch
        {
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
