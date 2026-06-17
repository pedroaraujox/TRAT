using System;
using System.IO;
using Newtonsoft.Json;

namespace WebstationBackup.Agent.Service.Core;

internal sealed class AgentState
{
    public string? LastWindowKey { get; set; }
    public DateTimeOffset? LastAttemptAtUtc { get; set; }
    public string? LastJobId { get; set; }
    public string? LastFinalState { get; set; }
    public DateTimeOffset? LastConfigReportAtUtc { get; set; }
}

internal sealed class AgentStateStore
{
    private readonly object _sync = new();
    private readonly string _path;

    public AgentStateStore(string path)
    {
        _path = path;
    }

    public AgentState Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return new AgentState();
            }

            var raw = File.ReadAllText(_path);
            var state = JsonConvert.DeserializeObject<AgentState>(raw);
            return state ?? new AgentState();
        }
    }

    public void Save(AgentState state)
    {
        lock (_sync)
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(state, Formatting.Indented));
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            File.Move(tmp, _path);
        }
    }
}
