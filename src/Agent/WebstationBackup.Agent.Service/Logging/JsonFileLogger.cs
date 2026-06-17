using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace WebstationBackup.Agent.Service.Logging;

internal interface ILogger
{
    void Info(string message, IDictionary<string, object?>? props = null);
    void Warn(string message, IDictionary<string, object?>? props = null);
    void Error(string message, Exception ex, IDictionary<string, object?>? props = null);
}

internal sealed class JsonFileLogger : ILogger
{
    private readonly object _sync = new();
    private readonly string _directoryPath;
    private readonly string _filePath;
    private readonly int _maxFileSizeBytes;
    private readonly int _maxFiles;

    public JsonFileLogger(string directoryPath, string fileName, int maxFileSizeMiB, int maxFiles)
    {
        _directoryPath = directoryPath;
        Directory.CreateDirectory(_directoryPath);
        _filePath = Path.Combine(_directoryPath, fileName);
        _maxFileSizeBytes = Math.Max(1, maxFileSizeMiB) * 1024 * 1024;
        _maxFiles = Math.Max(1, maxFiles);
    }

    public void Info(string message, IDictionary<string, object?>? props = null) => Write("INFO", message, null, props);
    public void Warn(string message, IDictionary<string, object?>? props = null) => Write("WARN", message, null, props);
    public void Error(string message, Exception ex, IDictionary<string, object?>? props = null) => Write("ERROR", message, ex, props);

    private void Write(string level, string message, Exception? ex, IDictionary<string, object?>? props)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tsUtc"] = DateTimeOffset.UtcNow,
            ["level"] = level,
            ["message"] = message
        };

        if (props is not null)
        {
            foreach (var kv in props)
            {
                payload[kv.Key] = kv.Value;
            }
        }

        if (ex is not null)
        {
            payload["exceptionType"] = ex.GetType().FullName;
            payload["exceptionMessage"] = ex.Message;
            payload["exceptionStackTrace"] = ex.StackTrace;
        }

        var line = JsonConvert.SerializeObject(payload) + Environment.NewLine;

        lock (_sync)
        {
            RotateIfNeeded();
            File.AppendAllText(_filePath, line, Encoding.UTF8);
        }
    }

    private void RotateIfNeeded()
    {
        var fi = new FileInfo(_filePath);
        if (!fi.Exists)
        {
            return;
        }

        if (fi.Length < _maxFileSizeBytes)
        {
            return;
        }

        for (var i = _maxFiles - 1; i >= 1; i--)
        {
            var src = $"{_filePath}.{i}";
            var dst = $"{_filePath}.{i + 1}";
            if (File.Exists(dst))
            {
                File.Delete(dst);
            }
            if (File.Exists(src))
            {
                File.Move(src, dst);
            }
        }

        var first = $"{_filePath}.1";
        if (File.Exists(first))
        {
            File.Delete(first);
        }

        File.Move(_filePath, first);
    }
}

