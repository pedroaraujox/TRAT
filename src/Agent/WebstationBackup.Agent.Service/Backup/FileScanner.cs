using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace WebstationBackup.Agent.Service.Backup;

internal sealed class FileScanner
{
    public IReadOnlyList<string> ScanFiles(IEnumerable<string> includePaths, IEnumerable<string> excludePaths)
    {
        var includes = includePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var excludes = excludePaths.Where(p => !string.IsNullOrWhiteSpace(p)).Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var results = new List<string>(capacity: 4096);
        foreach (var root in includes)
        {
            if (File.Exists(root))
            {
                if (!IsExcluded(root, excludes))
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
                if (IsExcluded(file, excludes))
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

    private static string NormalizePath(string p) => Path.GetFullPath(p.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
}

