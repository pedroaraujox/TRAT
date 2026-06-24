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

    public IReadOnlyList<string> ScanFiles(IEnumerable<string> includePaths, IEnumerable<string> excludePaths)
    {
        var includes = includePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var excludes = excludePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

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

            foreach (var file in EnumerateFilesSafe(root))
            {
                if (IsExcluded(file, excludes) || IsIgnoredMetadataFile(file))
                {
                    continue;
                }
                results.Add(file);
            }
        }

        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
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
            catch
            {
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
            catch
            {
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
}
