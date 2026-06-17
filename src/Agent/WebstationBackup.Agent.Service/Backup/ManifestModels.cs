using System;
using System.Collections.Generic;

namespace WebstationBackup.Agent.Service.Backup;

internal sealed class BackupManifest
{
    public required string JobId { get; init; }
    public required string CustomerId { get; init; }
    public required string HostId { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public required IReadOnlyList<ManifestItem> Items { get; init; }
    public required long PlannedBytes { get; init; }
    public required long PlannedItems { get; init; }
    public required ManifestIntegrityPolicy Integrity { get; init; }
}

internal sealed class ManifestIntegrityPolicy
{
    public required bool HashesEnabled { get; init; }
    public required long HashSmallFilesUpToBytes { get; init; }
    public required string Algorithm { get; init; }
}

internal sealed class ManifestItem
{
    public required string AbsolutePath { get; init; }
    public required string RelativePath { get; init; }
    public required long SizeBytes { get; init; }
    public required DateTimeOffset LastWriteTimeUtc { get; init; }
    public string? Sha256Base64 { get; init; }
}

