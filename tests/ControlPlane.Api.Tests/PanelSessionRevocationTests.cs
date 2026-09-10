using ControlPlane.Api.Data;
using ControlPlane.Api.Domain;
using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Tests;

public sealed class PanelSessionRevocationTests
{
    [Theory]
    [InlineData("disabled", 302)]
    [InlineData("deleted", 302)]
    [InlineData("password", 302)]
    [InlineData("demoted", 403)]
    [InlineData("active", 200)]
    public async Task ExistingSessionHonorsCurrentAccountState(string change, int status)
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        if (change != "deleted")
        {
            db.PanelUsers.Add(new PanelUser { Id = "user", Email = "test@example.test", DisplayName = "Test",
                Role = change == "demoted" ? "operator" : "admin", PasswordHash = "hash",
                PasswordSalt = change == "password" ? "rotated" : "salt", IsActive = change != "disabled",
                CreatedAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        var context = new DefaultHttpContext { Session = new MemorySession() };
        context.Request.Path = "/admin/panel/users";
        context.Session.SignInPanelUser("user", "test@example.test", "Test", "admin", "salt");
        var called = false;
        var middleware = new ApiTokenAuthMiddleware(_ => { called = true; return Task.CompletedTask; },
            new ConfigurationBuilder().Build(), provider.GetRequiredService<IServiceScopeFactory>());
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        Assert.Equal(status == 200, called);
        if (status == 302) Assert.Null(context.Session.GetString(PanelSecurityConstants.SessionUserId));
    }

    private sealed class MemorySession : ISession
    {
        private readonly Dictionary<string, byte[]> values = new();
        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => values.Keys;
        public void Clear() => values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => values.Remove(key);
        public void Set(string key, byte[] value) => values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => values.TryGetValue(key, out value!);
    }
}
