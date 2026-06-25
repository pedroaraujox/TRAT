using ControlPlane.Api.Controllers;
using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Dtos;
using ControlPlane.Api.Email;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ControlPlane.Api.Tests;

public sealed class AgentIngestControllerJobFinalTests
{
    [Fact]
    public async Task JobFinal_WhenRetried_DoesNotDuplicateArtifacts()
    {
        using var database = CreateDatabase();
        await SeedAsync(database.Context);

        var controller = CreateController(database.Context);
        var finishedAtUtc = DateTimeOffset.UtcNow;
        var report = new JobFinalReport(
            CustomerId: "customer-01",
            HostId: "host-01",
            JobId: "job-01",
            FinalState: "SUCCEEDED",
            PlannedBytes: 2048,
            PlannedItems: 2,
            UploadedBytes: 2048,
            UploadedItems: 2,
            FailureCode: null,
            FailureMessage: null,
            FinishedAtUtc: finishedAtUtc,
            Artifacts: new[]
            {
                new JobArtifactDto("MANIFEST_LOCAL", @"C:\ProgramData\TRAT\Agent\manifest.job-01.json")
            },
            RunRequestId: null);

        var first = await controller.JobFinal(report, CancellationToken.None);
        var second = await controller.JobFinal(report, CancellationToken.None);

        Assert.IsType<OkResult>(first);
        Assert.IsType<OkResult>(second);

        var job = await database.Context.Jobs.AsNoTracking().SingleAsync(j => j.Id == "job-01");
        Assert.Equal("SUCCEEDED", job.State);
        Assert.Equal(finishedAtUtc, job.FinishedAtUtc);

        var artifacts = await database.Context.Artifacts.AsNoTracking().Where(a => a.JobId == "job-01").ToListAsync();
        Assert.Single(artifacts);
        Assert.Equal("MANIFEST_LOCAL", artifacts[0].Type);
        Assert.Equal(@"C:\ProgramData\TRAT\Agent\manifest.job-01.json", artifacts[0].Location);
    }

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Customers.Add(new Customer
        {
            Id = "customer-01",
            Name = "Customer 01",
            AwsAccountId = "111111111111",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });

        db.Hosts.Add(new Host
        {
            Id = "host-01",
            CustomerId = "customer-01",
            Hostname = "HOST-01",
            OsVersion = "Windows Server",
            FirstSeenAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            LastHeartbeatAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });

        db.Jobs.Add(new Job
        {
            Id = "job-01",
            CustomerId = "customer-01",
            HostId = "host-01",
            State = "UPLOADING",
            StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5)
        });

        await db.SaveChangesAsync();
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
