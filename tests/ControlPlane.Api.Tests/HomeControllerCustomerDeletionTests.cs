using ControlPlane.Api.Controllers;
using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlPlane.Api.Tests;

public sealed class HomeControllerCustomerDeletionTests
{
    [Fact]
    public async Task DeleteCustomer_RemovesCustomerAndDependencies()
    {
        using var database = CreateDatabase();
        await SeedScenarioAsync(database.Context);
        var controller = CreateController(database.Context);

        var result = await controller.DeleteCustomer("customer-01", returnUrl: null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("/admin/customers", redirect.Url);
        Assert.Equal("Cliente excluido com sucesso.", controller.TempData["StatusMessage"]);

        Assert.Empty(await database.Context.Customers.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.Hosts.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.AgentConfigurations.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.BackupPolicies.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.PolicyChangeEvents.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.Jobs.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.Artifacts.AsNoTracking().ToListAsync());
        Assert.Empty(await database.Context.Alerts.AsNoTracking().ToListAsync());
    }

    private static HomeController CreateController(AppDbContext db)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(
            db,
            BuildConfiguration(),
            new AwsDiscoveryService(NullLogger<AwsDiscoveryService>.Instance),
            new HostOperationalStatusService(),
            NullLogger<HomeController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            },
            TempData = new TempDataDictionary(httpContext, new TestTempDataProvider())
        };

        return controller;
    }

    private static IConfiguration BuildConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
    }

    private static async Task SeedScenarioAsync(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;

        db.Customers.Add(new Customer
        {
            Id = "customer-01",
            Name = "Customer 01",
            AwsAccountId = "111111111111",
            AgentEnrollmentTokenHash = "token-hash",
            CreatedAtUtc = now.AddDays(-10)
        });

        db.Hosts.Add(new Host
        {
            Id = "host-01",
            CustomerId = "customer-01",
            Hostname = "HOST-01",
            OsVersion = "Windows Server 2022",
            FirstSeenAtUtc = now.AddDays(-5),
            LastHeartbeatAtUtc = now
        });

        db.AgentConfigurations.Add(new AgentConfiguration
        {
            Id = "cfg-01",
            CustomerId = "customer-01",
            HostId = "host-01",
            PolicyId = "policy-01",
            AgentVersion = "1.0.0",
            ServiceStatus = "Running",
            TlsMode = "TLS1.2",
            PrecheckTlsOk = true,
            PrecheckDiskOk = true,
            PrecheckCredentialOk = true,
            StagingPath = @"D:\BackupStaging",
            CredentialTargetName = "WebstationBackupAwsKeys",
            UploadMode = "direct-s3",
            LastConfigSyncAtUtc = now,
            LastPrecheckAtUtc = now,
            LastPrecheckMessage = "Prechecks OK.",
            CreatedAtUtc = now.AddMinutes(-15)
        });

        db.BackupPolicies.Add(new BackupPolicy
        {
            Id = "policy-01",
            CustomerId = "customer-01",
            Name = "Policy 01",
            PolicyKind = "operational",
            ScopeType = "host",
            HostId = "host-01",
            OriginHostId = null,
            IncludePathsCsv = @"C:\Dados",
            ExcludePathsCsv = null,
            ScheduleDaysCsv = "MON",
            StartTimeLocal = "22:00",
            MaxRuntimeMinutes = 120,
            CpuLimitPercent = 35,
            NetworkLimitMbit = 80,
            Enabled = true,
            LastChangedAtUtc = now.AddMinutes(-20),
            CreatedAtUtc = now.AddMinutes(-20)
        });

        db.PolicyChangeEvents.Add(new PolicyChangeEvent
        {
            Id = "event-01",
            PolicyId = "policy-01",
            EventType = "created",
            Message = "Policy created",
            CreatedAtUtc = now.AddMinutes(-19)
        });

        db.Jobs.Add(new Job
        {
            Id = "job-01",
            CustomerId = "customer-01",
            HostId = "host-01",
            State = "SUCCEEDED",
            StartedAtUtc = now.AddMinutes(-10),
            FinishedAtUtc = now.AddMinutes(-9),
            PlannedBytes = 100,
            PlannedItems = 1,
            UploadedBytes = 100,
            UploadedItems = 1
        });

        db.Artifacts.Add(new Artifact
        {
            Id = "artifact-01",
            JobId = "job-01",
            Type = "manifest",
            Location = "s3://bucket/manifest.json",
            CreatedAtUtc = now.AddMinutes(-9)
        });

        db.Alerts.Add(new Alert
        {
            Id = "alert-01",
            CustomerId = "customer-01",
            HostId = "host-01",
            JobId = "job-01",
            Type = "JOB_FAILED",
            Severity = "CRITICAL",
            Message = "Failure",
            CreatedAtUtc = now.AddMinutes(-8)
        });

        await db.SaveChangesAsync();
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

        return new TestDatabase(connection, context);
    }

    private sealed class TestDatabase(SqliteConnection connection, AppDbContext context) : IDisposable
    {
        public AppDbContext Context { get; } = context;

        public void Dispose()
        {
            Context.Dispose();
            connection.Dispose();
        }
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }
}
