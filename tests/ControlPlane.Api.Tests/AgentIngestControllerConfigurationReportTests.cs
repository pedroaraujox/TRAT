using ControlPlane.Api.Controllers;
using ControlPlane.Api.Data;
using ControlPlane.Api.Dtos;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Email;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlPlane.Api.Tests;

public sealed class AgentIngestControllerConfigurationReportTests
{
    [Fact]
    public async Task CompleteRunRequest_CompletesAwsCheckWithoutCreatingJob()
    {
        using var database = CreateDatabase();
        database.Context.AgentRunRequests.Add(new AgentRunRequest
        {
            Id = "aws-check-01",
            CustomerId = "customer-01",
            HostId = "host-01",
            TriggerType = "aws_check",
            State = "CLAIMED",
            RequestedBy = "admin@example.invalid",
            RequestedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            ClaimedAtUtc = DateTimeOffset.UtcNow
        });
        await database.Context.SaveChangesAsync();
        var controller = CreateController(database.Context);

        var result = await controller.CompleteRunRequest(
            new AgentIngestController.CompleteRunRequestRequest("customer-01", "host-01", "aws-check-01", true, "ok"),
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var request = await database.Context.AgentRunRequests.AsNoTracking().SingleAsync();
        Assert.Equal("COMPLETED", request.State);
        Assert.NotNull(request.CompletedAtUtc);
        Assert.Null(request.FailureMessage);
        Assert.Empty(await database.Context.Jobs.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ConfigurationReport_PersistsEffectivePolicyMetadata()
    {
        using var database = CreateDatabase();
        var controller = CreateController(database.Context);

        var timestamp = DateTimeOffset.UtcNow;
        var result = await controller.ConfigurationReport(
            new AgentConfigurationReportRequest(
                CustomerId: "customer-01",
                HostId: "host-01",
                EffectivePolicyId: "policy-bootstrap-01",
                EffectivePolicyName: "Politica Inicial - HOST-01",
                EffectivePolicyKind: "bootstrap",
                EffectivePolicySource: "configuration_assignment",
                EffectivePolicyLastChangedAtUtc: timestamp.AddMinutes(-15),
                AgentVersion: "1.0.0",
                ServiceStatus: "Running",
                TlsMode: "TLS1.2",
                PrecheckTlsOk: true,
                PrecheckDiskOk: true,
                PrecheckCredentialOk: true,
                StagingPath: @"D:\Staging",
                CredentialTargetName: "TRATAwsKeys",
                UploadMode: "direct-s3",
                TimestampUtc: timestamp,
                PrecheckAtUtc: timestamp,
                PrecheckMessage: "Prechecks OK. PolicySource=configuration_assignment PolicyId=policy-bootstrap-01",
                AwsAccountId: "123456789012",
                BucketDiscoveryOk: true,
                BucketDiscoveryMessage: "A credencial AWS reportou 2 bucket(s) visivel(is).",
                AvailableBuckets: ["bucket-b", "bucket-a", "bucket-a"],
                AvailableBucketRegions: new Dictionary<string, string>
                {
                    ["bucket-b"] = "sa-east-1",
                    ["bucket-a"] = "us-east-1"
                }),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);

        var persisted = await database.Context.AgentConfigurations.AsNoTracking().SingleAsync();
        Assert.Equal("policy-bootstrap-01", persisted.EffectivePolicyId);
        Assert.Equal("Politica Inicial - HOST-01", persisted.EffectivePolicyName);
        Assert.Equal("bootstrap", persisted.EffectivePolicyKind);
        Assert.Equal("configuration_assignment", persisted.EffectivePolicySource);
        Assert.Equal(timestamp.AddMinutes(-15), persisted.EffectivePolicyLastChangedAtUtc);
        Assert.Equal("123456789012", persisted.AwsAccountId);
        Assert.True(persisted.BucketDiscoveryOk);
        Assert.Equal("A credencial AWS reportou 2 bucket(s) visivel(is).", persisted.BucketDiscoveryMessage);
        Assert.Equal("bucket-a,bucket-b", persisted.AvailableBucketsCsv);
        Assert.Equal("{\"bucket-a\":\"us-east-1\",\"bucket-b\":\"sa-east-1\"}", persisted.AvailableBucketRegionsJson);
    }

    [Fact]
    public async Task ConfigurationReport_UpdatesAndClearsEffectivePolicyMetadata()
    {
        using var database = CreateDatabase();
        var controller = CreateController(database.Context);

        var firstTimestamp = DateTimeOffset.UtcNow.AddMinutes(-5);
        await controller.ConfigurationReport(
            new AgentConfigurationReportRequest(
                CustomerId: "customer-01",
                HostId: "host-01",
                EffectivePolicyId: "policy-bootstrap-01",
                EffectivePolicyName: "Politica Inicial - HOST-01",
                EffectivePolicyKind: "bootstrap",
                EffectivePolicySource: "configuration_assignment",
                EffectivePolicyLastChangedAtUtc: firstTimestamp.AddMinutes(-10),
                AgentVersion: "1.0.0",
                ServiceStatus: "Running",
                TlsMode: "TLS1.2",
                PrecheckTlsOk: true,
                PrecheckDiskOk: true,
                PrecheckCredentialOk: true,
                StagingPath: @"D:\Staging",
                CredentialTargetName: "TRATAwsKeys",
                UploadMode: "direct-s3",
                TimestampUtc: firstTimestamp,
                PrecheckAtUtc: firstTimestamp,
                PrecheckMessage: "Prechecks OK."),
            CancellationToken.None);

        var secondTimestamp = DateTimeOffset.UtcNow;
        await controller.ConfigurationReport(
            new AgentConfigurationReportRequest(
                CustomerId: "customer-01",
                HostId: "host-01",
                EffectivePolicyId: null,
                EffectivePolicyName: null,
                EffectivePolicyKind: "local_fallback",
                EffectivePolicySource: "local_fallback",
                EffectivePolicyLastChangedAtUtc: null,
                AgentVersion: "1.0.1",
                ServiceStatus: "DryRun",
                TlsMode: "TLS1.2",
                PrecheckTlsOk: true,
                PrecheckDiskOk: true,
                PrecheckCredentialOk: false,
                StagingPath: @"D:\Staging",
                CredentialTargetName: "TRATAwsKeys",
                UploadMode: "dry-run",
                TimestampUtc: secondTimestamp,
                PrecheckAtUtc: secondTimestamp,
                PrecheckMessage: "AWS precheck nao executado."),
            CancellationToken.None);

        var persisted = await database.Context.AgentConfigurations.AsNoTracking().SingleAsync();
        Assert.Null(persisted.EffectivePolicyId);
        Assert.Null(persisted.EffectivePolicyName);
        Assert.Equal("local_fallback", persisted.EffectivePolicyKind);
        Assert.Equal("local_fallback", persisted.EffectivePolicySource);
        Assert.Null(persisted.EffectivePolicyLastChangedAtUtc);
        Assert.Equal("1.0.1", persisted.AgentVersion);
        Assert.Equal("DryRun", persisted.ServiceStatus);
        Assert.False(persisted.PrecheckCredentialOk);
    }

    [Fact]
    public async Task ConfigurationReport_PersistsBucketDiscoveryFailureWithoutExposingCredentials()
    {
        using var database = CreateDatabase();
        var controller = CreateController(database.Context);
        var timestamp = DateTimeOffset.UtcNow;

        await controller.ConfigurationReport(
            new AgentConfigurationReportRequest(
                CustomerId: "customer-01",
                HostId: "host-01",
                EffectivePolicyId: null,
                EffectivePolicyName: null,
                EffectivePolicyKind: "local_fallback",
                EffectivePolicySource: "local_fallback",
                EffectivePolicyLastChangedAtUtc: null,
                AgentVersion: "1.0.0",
                ServiceStatus: "Running",
                TlsMode: "TLS1.2",
                PrecheckTlsOk: true,
                PrecheckDiskOk: true,
                PrecheckCredentialOk: true,
                StagingPath: "N/A",
                CredentialTargetName: "DPAPI:LocalMachine",
                UploadMode: "direct-s3",
                TimestampUtc: timestamp,
                PrecheckAtUtc: timestamp,
                PrecheckMessage: "AWS validado (identidade).",
                AwsAccountId: "123456789012",
                BucketDiscoveryOk: false,
                BucketDiscoveryMessage: "A AWS negou a listagem de buckets. Confirme a permissao s3:ListAllMyBuckets."),
            CancellationToken.None);

        var persisted = await database.Context.AgentConfigurations.AsNoTracking().SingleAsync();
        Assert.Equal("123456789012", persisted.AwsAccountId);
        Assert.False(persisted.BucketDiscoveryOk);
        Assert.Equal(
            "A AWS negou a listagem de buckets. Confirme a permissao s3:ListAllMyBuckets.",
            persisted.BucketDiscoveryMessage);
        Assert.Null(persisted.AvailableBucketsCsv);
    }

    private static AgentIngestController CreateController(AppDbContext db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:Smtp:Enabled"] = "false"
            })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Items["AgentCustomerId"] = "customer-01";

        var hostStatus = new HostOperationalStatusService();
        var operationalAlerts = new OperationalAlertService(db, hostStatus, NullLogger<OperationalAlertService>.Instance);

        return new AgentIngestController(
            db,
            new SmtpEmailSender(configuration, NullLogger<SmtpEmailSender>.Instance),
            new PolicyResolutionService(db, NullLogger<PolicyResolutionService>.Instance),
            operationalAlerts,
            NullLogger<AgentIngestController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static TestDatabase CreateDatabase()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        SchemaBootstrapper.EnsureExtendedSchemaAsync(context).GetAwaiter().GetResult();
        return new TestDatabase(context, connection);
    }

    private sealed class TestDatabase : IDisposable
    {
        public TestDatabase(AppDbContext context, SqliteConnection connection)
        {
            Context = context;
            Connection = connection;
        }

        public AppDbContext Context { get; }
        public SqliteConnection Connection { get; }

        public void Dispose()
        {
            Context.Dispose();
            Connection.Dispose();
        }
    }
}

