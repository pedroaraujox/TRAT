using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ControlPlane.Api.Seed;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (await db.Customers.AnyAsync() || await db.Hosts.AnyAsync() || await db.Jobs.AnyAsync())
        {
            await SeedExtendedAsync(db);
            return;
        }

        var customer = new Customer
        {
            Id = "cliente-demo",
            Name = "Cliente Demo",
            AwsAccountId = "123456789012",
            NotificationEmailsCsv = "admin@webstation.local",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10)
        };

        var hostA = new ControlPlane.Api.Domain.Host
        {
            Id = "host-demo-01",
            CustomerId = customer.Id,
            Hostname = "SRV-FILES-01",
            OsVersion = "Windows Server 2019",
            FirstSeenAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
            LastHeartbeatAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2)
        };

        var hostB = new ControlPlane.Api.Domain.Host
        {
            Id = "host-demo-02",
            CustomerId = customer.Id,
            Hostname = "SRV-ERP-01",
            OsVersion = "Windows Server 2012 R2",
            FirstSeenAtUtc = DateTimeOffset.UtcNow.AddDays(-10),
            LastHeartbeatAtUtc = DateTimeOffset.UtcNow.AddMinutes(-12)
        };

        var okJob = new Job
        {
            Id = "job-demo-ok",
            CustomerId = customer.Id,
            HostId = hostA.Id,
            State = "SUCCEEDED",
            StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-10),
            FinishedAtUtc = DateTimeOffset.UtcNow.AddHours(-9).AddMinutes(-12),
            PlannedBytes = 150L * 1024 * 1024 * 1024,
            UploadedBytes = 150L * 1024 * 1024 * 1024,
            PlannedItems = 180234,
            UploadedItems = 180234
        };

        var failedJob = new Job
        {
            Id = "job-demo-failed",
            CustomerId = customer.Id,
            HostId = hostB.Id,
            State = "FAILED",
            StartedAtUtc = DateTimeOffset.UtcNow.AddHours(-34),
            FinishedAtUtc = DateTimeOffset.UtcNow.AddHours(-33).AddMinutes(-10),
            PlannedBytes = 52L * 1024 * 1024 * 1024,
            UploadedBytes = 41L * 1024 * 1024 * 1024,
            PlannedItems = 68123,
            UploadedItems = 53002,
            FailureCode = "BYTES_MISMATCH",
            FailureMessage = "Bytes enviados diferem do planejado."
        };

        var alert = new Alert
        {
            Id = "alert-demo-1",
            CustomerId = customer.Id,
            HostId = hostB.Id,
            JobId = failedJob.Id,
            RootCauseKey = $"job-runtime:{customer.Id}:{hostB.Id}:bytes-mismatch",
            Source = "job_runtime",
            Type = "JOB_FAILED",
            Severity = "CRITICAL",
            Message = "Job do host SRV-ERP-01 finalizado com divergencia de bytes.",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-33),
            LastObservedAtUtc = DateTimeOffset.UtcNow.AddHours(-33)
        };

        var artifacts = new[]
        {
            new Artifact
            {
                Id = "artifact-demo-1",
                JobId = okJob.Id,
                Type = "MANIFEST_LOCAL",
                Location = @"C:\ProgramData\WebstationBackup\Agent\manifest.job-demo-ok.json",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-9)
            },
            new Artifact
            {
                Id = "artifact-demo-2",
                JobId = failedJob.Id,
                Type = "MANIFEST_LOCAL",
                Location = @"C:\ProgramData\WebstationBackup\Agent\manifest.job-demo-failed.json",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddHours(-33)
            }
        };

        db.Customers.Add(customer);
        db.Hosts.AddRange(hostA, hostB);
        db.Jobs.AddRange(okJob, failedJob);
        db.Alerts.Add(alert);
        db.Artifacts.AddRange(artifacts);

        await db.SaveChangesAsync();
        await SeedExtendedAsync(db);
    }

    private static async Task SeedExtendedAsync(AppDbContext db)
    {
        if (!await db.BackupPolicies.AnyAsync())
        {
            db.BackupPolicies.AddRange(
                new BackupPolicy
                {
                    Id = "policy-demo-default",
                    CustomerId = "cliente-demo",
                    Name = "Politica Noturna Padrao",
                    PolicyKind = "operational",
                    ScopeType = "customer",
                    HostId = null,
                    OriginHostId = null,
                    IncludePathsCsv = @"D:\Dados;C:\Webstation",
                    ExcludePathsCsv = @"D:\Dados\Temp",
                    ScheduleDaysCsv = "TUE,FRI",
                    StartTimeLocal = "22:00",
                    MaxRuntimeMinutes = 720,
                    CpuLimitPercent = 35,
                    NetworkLimitMbit = 80,
                    Enabled = true,
                    LastChangedAtUtc = DateTimeOffset.UtcNow.AddDays(-8),
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-8)
                },
                new BackupPolicy
                {
                    Id = "policy-demo-erp",
                    CustomerId = "cliente-demo",
                    Name = "ERP Critico",
                    PolicyKind = "operational",
                    ScopeType = "host",
                    HostId = "host-demo-02",
                    OriginHostId = null,
                    IncludePathsCsv = @"D:\ERP\Backup;D:\ERP\Exports",
                    ExcludePathsCsv = @"D:\ERP\Temp",
                    ScheduleDaysCsv = "MON,TUE,WED,THU,FRI",
                    StartTimeLocal = "22:30",
                    MaxRuntimeMinutes = 600,
                    CpuLimitPercent = 25,
                    NetworkLimitMbit = 60,
                    Enabled = true,
                    LastChangedAtUtc = DateTimeOffset.UtcNow.AddDays(-7),
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-7)
                });
        }

        if (!await db.AgentConfigurations.AnyAsync())
        {
            db.AgentConfigurations.AddRange(
                new AgentConfiguration
                {
                    Id = "config-demo-01",
                    CustomerId = "cliente-demo",
                    HostId = "host-demo-01",
                    PolicyId = "policy-demo-default",
                    AgentVersion = "1.0.0",
                    ServiceStatus = "Running",
                    TlsMode = "TLS1.2",
                    PrecheckTlsOk = true,
                    PrecheckDiskOk = true,
                    PrecheckCredentialOk = true,
                    StagingPath = @"D:\BackupStaging",
                    CredentialTargetName = "WebstationBackupAwsKeys",
                    UploadMode = "direct-s3",
                    LastConfigSyncAtUtc = DateTimeOffset.UtcNow.AddMinutes(-20),
                    LastPrecheckAtUtc = DateTimeOffset.UtcNow.AddMinutes(-30),
                    LastPrecheckMessage = "Prechecks concluidos com sucesso.",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-8)
                },
                new AgentConfiguration
                {
                    Id = "config-demo-02",
                    CustomerId = "cliente-demo",
                    HostId = "host-demo-02",
                    PolicyId = "policy-demo-erp",
                    AgentVersion = "1.0.0",
                    ServiceStatus = "Warning",
                    TlsMode = "TLS1.2",
                    PrecheckTlsOk = true,
                    PrecheckDiskOk = false,
                    PrecheckCredentialOk = true,
                    StagingPath = @"C:\BackupStaging",
                    CredentialTargetName = "WebstationBackupAwsKeys",
                    UploadMode = "direct-s3",
                    LastConfigSyncAtUtc = DateTimeOffset.UtcNow.AddHours(-3),
                    LastPrecheckAtUtc = DateTimeOffset.UtcNow.AddHours(-2),
                    LastPrecheckMessage = "Pouco espaco livre no volume de staging.",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-7)
                });
        }

        await db.SaveChangesAsync();
    }
}
