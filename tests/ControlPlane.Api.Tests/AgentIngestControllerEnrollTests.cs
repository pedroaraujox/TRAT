using ControlPlane.Api.Controllers;
using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Email;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text;
 
namespace ControlPlane.Api.Tests;
 
public sealed class AgentIngestControllerEnrollTests
{
    [Fact]
    public async Task Enroll_WhenHostIdAlreadyUsedByAnotherCustomer_MintsScopedHostId()
    {
        using var database = CreateDatabase();
        await SeedCustomersAsync(database.Context);
 
        const string requestedHostId = "WEBSTATION-01";
 
        var controllerA = CreateController(database.Context, customerId: "customer-a");
        var resultA = await controllerA.Enroll(
            new AgentIngestController.AgentEnrollRequest(
                HostId: requestedHostId,
                Hostname: "WEBSTATION-01",
                OsVersion: "Windows"),
            CancellationToken.None);
 
        var okA = Assert.IsType<OkObjectResult>(resultA);
        var payloadA = Assert.IsType<AgentIngestController.AgentEnrollResponse>(okA.Value);
        Assert.Equal("customer-a", payloadA.CustomerId);
        Assert.Equal(requestedHostId, payloadA.HostId);
 
        var controllerB = CreateController(database.Context, customerId: "customer-b");
        var resultB = await controllerB.Enroll(
            new AgentIngestController.AgentEnrollRequest(
                HostId: requestedHostId,
                Hostname: "WEBSTATION-01",
                OsVersion: "Windows"),
            CancellationToken.None);
 
        var okB = Assert.IsType<OkObjectResult>(resultB);
        var payloadB = Assert.IsType<AgentIngestController.AgentEnrollResponse>(okB.Value);
        Assert.Equal("customer-b", payloadB.CustomerId);
        Assert.NotEqual(requestedHostId, payloadB.HostId);
        Assert.Equal(BuildExpectedScopedHostId(customerId: "customer-b", requestedHostId), payloadB.HostId);
 
        var hosts = await database.Context.Hosts.AsNoTracking().OrderBy(h => h.CustomerId).ToListAsync();
        Assert.Equal(2, hosts.Count);
        Assert.Equal("customer-a", hosts[0].CustomerId);
        Assert.Equal(requestedHostId, hosts[0].Id);
        Assert.Equal("customer-b", hosts[1].CustomerId);
        Assert.Equal(payloadB.HostId, hosts[1].Id);
    }
 
    private static async Task SeedCustomersAsync(AppDbContext db)
    {
        db.Customers.AddRange(
            new Customer
            {
                Id = "customer-a",
                Name = "Customer A",
                AwsAccountId = "111111111111",
                CreatedAtUtc = DateTimeOffset.UtcNow
            },
            new Customer
            {
                Id = "customer-b",
                Name = "Customer B",
                AwsAccountId = "222222222222",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
 
        await db.SaveChangesAsync();
    }
 
    private static AgentIngestController CreateController(AppDbContext db, string customerId)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:Smtp:Enabled"] = "false"
            })
            .Build();
 
        var httpContext = new DefaultHttpContext();
        httpContext.Items["AgentCustomerId"] = customerId;
 
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
 
    private static string BuildExpectedScopedHostId(string customerId, string requestedHostId)
    {
        var bytes = Encoding.UTF8.GetBytes(customerId.Trim());
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        var suffix = hex.Substring(0, 10);
        return requestedHostId.Trim() + "--" + suffix;
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
