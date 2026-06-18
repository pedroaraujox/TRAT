using ControlPlane.Api.Controllers;
using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Models;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlPlane.Api.Tests;

public sealed class HomeControllerPolicyAssignmentTests
{
    [Fact]
    public async Task UpdateConfigurationPolicy_BlocksAssignment_WhenHostIsNotReady()
    {
        using var database = CreateDatabase();
        await SeedScenarioAsync(database.Context, precheckCredentialOk: false);
        var controller = CreateController(database.Context);

        var result = await controller.UpdateConfigurationPolicy("cfg-01", "policy-01", returnUrl: null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        var persisted = await database.Context.AgentConfigurations.AsNoTracking().SingleAsync();

        Assert.Equal("/admin/configurations/cfg-01", redirect.Url);
        Assert.Null(persisted.PolicyId);
        Assert.Equal("Falha AWS.", controller.TempData["ErrorMessage"]);
    }

    [Fact]
    public async Task UpdateConfigurationPolicy_PersistsAssignment_WhenHostIsReady()
    {
        using var database = CreateDatabase();
        await SeedScenarioAsync(database.Context, precheckCredentialOk: true);
        var controller = CreateController(database.Context);

        var result = await controller.UpdateConfigurationPolicy("cfg-01", "policy-01", returnUrl: null, CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        var persisted = await database.Context.AgentConfigurations.AsNoTracking().SingleAsync();

        Assert.Equal("/admin/configurations/cfg-01", redirect.Url);
        Assert.Equal("policy-01", persisted.PolicyId);
        Assert.Equal("Politica vinculada com sucesso.", controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task CreateBootstrapPolicy_CreatesAndAssigns_WhenHostIsReady()
    {
        using var database = CreateDatabase();
        await SeedScenarioAsync(database.Context, precheckCredentialOk: true);
        var controller = CreateController(database.Context);

        var result = await controller.CreateBootstrapPolicy(
            "cfg-01",
            new BootstrapPolicyDraftViewModel
            {
                SuggestedPolicyId = "policy-bootstrap-01",
                Name = "Politica Inicial - HOST-01",
                ScopeType = "host",
                CustomerId = "customer-01",
                HostId = "host-01",
                IncludePathsCsv = @"C:\Dados;D:\ERP",
                ExcludePathsCsv = @"C:\Dados\Temp",
                ScheduleDaysCsv = "MON,TUE",
                StartTimeLocal = "21:30",
                MaxRuntimeMinutes = 180,
                CpuLimitPercent = 20,
                NetworkLimitMbit = 40,
                Enabled = true,
                HasBootstrapPaths = true
            },
            returnUrl: null,
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        var persistedConfig = await database.Context.AgentConfigurations.AsNoTracking().SingleAsync();
        var persistedPolicy = await database.Context.BackupPolicies.AsNoTracking().SingleAsync(p => p.Id == "policy-bootstrap-01");
        var events = (await database.Context.PolicyChangeEvents.AsNoTracking()
            .Where(e => e.PolicyId == "policy-bootstrap-01")
            .ToListAsync())
            .OrderBy(e => e.CreatedAtUtc)
            .ToList();

        Assert.Equal("/admin/configurations/cfg-01", redirect.Url);
        Assert.Equal("policy-bootstrap-01", persistedConfig.PolicyId);
        Assert.Equal("Politica Inicial - HOST-01", persistedPolicy.Name);
        Assert.Equal("bootstrap", persistedPolicy.PolicyKind);
        Assert.Equal("host-01", persistedPolicy.OriginHostId);
        Assert.Equal(@"C:\Dados;D:\ERP", persistedPolicy.IncludePathsCsv);
        Assert.Contains(events, e => e.EventType == "bootstrap_policy_created");
        Assert.Contains(events, e => e.EventType == "bootstrap_policy_auto_assigned");
        Assert.Equal("Politica inicial criada a partir do bootstrap e vinculada ao host.", controller.TempData["StatusMessage"]);
    }

    [Fact]
    public async Task CreateBootstrapPolicy_CreatesWithoutAssigning_WhenHostIsNotReady()
    {
        using var database = CreateDatabase();
        await SeedScenarioAsync(database.Context, precheckCredentialOk: false);
        var controller = CreateController(database.Context);

        var result = await controller.CreateBootstrapPolicy(
            "cfg-01",
            new BootstrapPolicyDraftViewModel
            {
                SuggestedPolicyId = "policy-bootstrap-02",
                Name = "Politica Inicial - HOST-01",
                ScopeType = "host",
                CustomerId = "customer-01",
                HostId = "host-01",
                IncludePathsCsv = @"C:\Dados",
                ExcludePathsCsv = null,
                ScheduleDaysCsv = "MON,TUE",
                StartTimeLocal = "21:30",
                MaxRuntimeMinutes = 180,
                CpuLimitPercent = 20,
                NetworkLimitMbit = 40,
                Enabled = true,
                HasBootstrapPaths = true
            },
            returnUrl: null,
            CancellationToken.None);

        var redirect = Assert.IsType<RedirectResult>(result);
        var persistedConfig = await database.Context.AgentConfigurations.AsNoTracking().SingleAsync();
        var persistedPolicy = await database.Context.BackupPolicies.AsNoTracking().SingleAsync(p => p.Id == "policy-bootstrap-02");
        var events = (await database.Context.PolicyChangeEvents.AsNoTracking()
            .Where(e => e.PolicyId == "policy-bootstrap-02")
            .ToListAsync())
            .OrderBy(e => e.CreatedAtUtc)
            .ToList();

        Assert.Equal("/admin/configurations/cfg-01", redirect.Url);
        Assert.Null(persistedConfig.PolicyId);
        Assert.Equal("policy-bootstrap-02", persistedPolicy.Id);
        Assert.Equal("bootstrap", persistedPolicy.PolicyKind);
        Assert.Contains(events, e => e.EventType == "bootstrap_policy_created");
        Assert.DoesNotContain(events, e => e.EventType == "bootstrap_policy_auto_assigned");
        Assert.Equal("Politica inicial criada a partir do bootstrap. O vinculo automatico ficou pendente porque o host ainda nao esta pronto.", controller.TempData["StatusMessage"]);
    }

    private static HomeController CreateController(AppDbContext db)
    {
        var httpContext = new DefaultHttpContext();
        var controller = new HomeController(
            db,
            BuildConfiguration(),
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

    private static async Task SeedScenarioAsync(AppDbContext db, bool precheckCredentialOk)
    {
        var now = DateTimeOffset.UtcNow;

        db.Hosts.Add(new Host
        {
            Id = "host-01",
            CustomerId = "customer-01",
            Hostname = "HOST-01",
            OsVersion = "Windows Server 2022",
            BootstrapIncludePathsCsv = @"C:\Dados;D:\ERP",
            BootstrapExcludePathsCsv = @"C:\Dados\Temp",
            FirstSeenAtUtc = now.AddDays(-5),
            LastHeartbeatAtUtc = now
        });

        db.AgentConfigurations.Add(new AgentConfiguration
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
            CredentialTargetName = "WebstationBackupAwsKeys",
            UploadMode = "direct-s3",
            LastConfigSyncAtUtc = now,
            LastPrecheckAtUtc = now,
            LastPrecheckMessage = precheckCredentialOk ? "Prechecks OK." : "Falha AWS.",
            CreatedAtUtc = now.AddMinutes(-5)
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
            ExcludePathsCsv = @"C:\Dados\Temp",
            ScheduleDaysCsv = "MON,TUE,WED",
            StartTimeLocal = "22:00",
            MaxRuntimeMinutes = 120,
            CpuLimitPercent = 25,
            NetworkLimitMbit = 50,
            Enabled = true,
            LastChangedAtUtc = now.AddMinutes(-10),
            CreatedAtUtc = now.AddMinutes(-10)
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
