using ControlPlane.Api.Domain;
using ControlPlane.Api.Services;

namespace ControlPlane.Api.Tests;

public sealed class HostOperationalStatusServiceTests
{
    private readonly HostOperationalStatusService service = new();

    [Fact]
    public void Evaluate_ReturnsPendingAws_WhenCredentialPrecheckFails()
    {
        var host = CreateHost(lastHeartbeatAtUtc: DateTimeOffset.UtcNow);
        var configuration = CreateConfiguration(precheckCredentialOk: false);

        var status = service.Evaluate(host, configuration);

        Assert.Equal("Pendente AWS", status.Label);
        Assert.Equal("warning", status.CssClass);
        Assert.Equal("Falha AWS.", status.Message);
        Assert.False(status.IsReadyForPolicyAssignment);
    }

    [Fact]
    public void Evaluate_ReturnsReady_WhenHostIsOnlineAndAllPrechecksPass()
    {
        var host = CreateHost(lastHeartbeatAtUtc: DateTimeOffset.UtcNow);
        var configuration = CreateConfiguration(precheckCredentialOk: true);

        var status = service.Evaluate(host, configuration);

        Assert.Equal("Online", status.HeartbeatStatus);
        Assert.Equal("Pronto", status.Label);
        Assert.Equal("ready", status.CssClass);
        Assert.True(status.IsReadyForPolicyAssignment);
    }

    private static Host CreateHost(DateTimeOffset? lastHeartbeatAtUtc)
    {
        return new Host
        {
            Id = "host-01",
            CustomerId = "customer-01",
            Hostname = "HOST-01",
            OsVersion = "Windows Server 2022",
            FirstSeenAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
            LastHeartbeatAtUtc = lastHeartbeatAtUtc
        };
    }

    private static AgentConfiguration CreateConfiguration(bool precheckCredentialOk)
    {
        return new AgentConfiguration
        {
            Id = "cfg-01",
            CustomerId = "customer-01",
            HostId = "host-01",
            PolicyId = null,
            AgentVersion = "1.0.0",
            ServiceStatus = "Running",
            TlsMode = "TLS1.2",
            PrecheckTlsOk = true,
            PrecheckDiskOk = true,
            PrecheckCredentialOk = precheckCredentialOk,
            StagingPath = @"D:\BackupStaging",
            CredentialTargetName = "TRATAwsKeys",
            UploadMode = "direct-s3",
            LastConfigSyncAtUtc = DateTimeOffset.UtcNow,
            LastPrecheckAtUtc = DateTimeOffset.UtcNow,
            LastPrecheckMessage = precheckCredentialOk ? "Prechecks OK." : "Falha AWS.",
            CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10)
        };
    }
}

