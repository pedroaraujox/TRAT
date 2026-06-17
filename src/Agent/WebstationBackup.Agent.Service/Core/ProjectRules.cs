using System;
using System.IO;
using Newtonsoft.Json;

namespace WebstationBackup.Agent.Service.Core;

internal sealed class ProjectRules
{
    public required int SchemaVersion { get; init; }
    public required Defaults Defaults { get; init; }
    public required SecurityAndSafety SecurityAndSafety { get; init; }
    public required JobDefinition JobDefinition { get; init; }

    public static ProjectRules LoadOrThrow(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Arquivo project.rules.json não encontrado.", path);
        }

        var raw = File.ReadAllText(path);
        var rules = JsonConvert.DeserializeObject<ProjectRules>(raw);
        if (rules is null)
        {
            throw new InvalidOperationException("Falha ao desserializar ProjectRules.");
        }

        if (rules.SchemaVersion <= 0)
        {
            throw new InvalidOperationException("SchemaVersion inválido em project.rules.json.");
        }

        return rules;
    }
}

internal sealed class SecurityAndSafety
{
    public required AwsSecurity Aws { get; init; }
    public required RansomwarePolicy Ransomware { get; init; }
}

internal sealed class AwsSecurity
{
    public required bool NeverDeleteFromS3 { get; init; }
}

internal sealed class RansomwarePolicy
{
    public required AnomalyDetectionPolicy AnomalyDetection { get; init; }
}

internal sealed class AnomalyDetectionPolicy
{
    public required bool Enabled { get; init; }
    public required int ChangeRateAlertThresholdPercent { get; init; }
    public required string ActionOnAnomaly { get; init; }
}

internal sealed class JobDefinition
{
    public required OkCriteria OkCriteria { get; init; }
}

internal sealed class OkCriteria
{
    public required bool ManifestRequired { get; init; }
    public required bool MustUploadAllManifestItems { get; init; }
    public required bool BytesPlannedMustEqualBytesConfirmedOnS3 { get; init; }
    public required bool CountPlannedMustEqualCountConfirmedOnS3 { get; init; }
    public required IntegrityCriteria Integrity { get; init; }
}

internal sealed class IntegrityCriteria
{
    public required bool RequireChecksumWhenAvailable { get; init; }
    public required string PreferredChecksum { get; init; }
}

internal sealed class Defaults
{
    public required ScheduleDefaults Schedule { get; init; }
    public required ScanDefaults Scan { get; init; }
    public required UploadDefaults Upload { get; init; }
    public required ThrottlingDefaults Throttling { get; init; }
    public required LoggingDefaults Logging { get; init; }
    public required CompatibilityDefaults Compatibility { get; init; }
}

internal sealed class ScheduleDefaults
{
    public required bool Enabled { get; init; }
    public required string Timezone { get; init; }
    public required string[] DaysOfWeek { get; init; }
    public required string StartTimeLocal { get; init; }
    public required int MaxRuntimeMinutes { get; init; }
}

internal sealed class ScanDefaults
{
    public required bool UseFileTimestampAndSizeForChangeDetection { get; init; }
    public required HashDefaults UseHashes { get; init; }
}

internal sealed class HashDefaults
{
    public required bool Enabled { get; init; }
    public required long HashSmallFilesUpToBytes { get; init; }
}

internal sealed class UploadDefaults
{
    public required MultipartDefaults Multipart { get; init; }
    public required int MaxConcurrentFiles { get; init; }
    public required RetryDefaults Retry { get; init; }
}

internal sealed class MultipartDefaults
{
    public required bool Enabled { get; init; }
    public required int PartSizeMiB { get; init; }
    public required int MaxConcurrentParts { get; init; }
}

internal sealed class RetryDefaults
{
    public required int MaxAttempts { get; init; }
    public required string Backoff { get; init; }
    public required int InitialDelaySeconds { get; init; }
    public required int MaxDelaySeconds { get; init; }
}

internal sealed class ThrottlingDefaults
{
    public required int CpuMaxPercent { get; init; }
    public required int NetworkMaxMbit { get; init; }
}

internal sealed class LoggingDefaults
{
    public required LocalLoggingDefaults Local { get; init; }
}

internal sealed class LocalLoggingDefaults
{
    public required bool Enabled { get; init; }
    public required string Level { get; init; }
    public required int MaxFileSizeMiB { get; init; }
    public required int MaxFiles { get; init; }
}

internal sealed class CompatibilityDefaults
{
    public required TlsDefaults Tls { get; init; }
}

internal sealed class TlsDefaults
{
    public required string MinimumVersion { get; init; }
    public required bool PrecheckMustFailIfNotSupported { get; init; }
}
