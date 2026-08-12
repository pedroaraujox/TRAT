using ControlPlane.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ControlPlane.Api.Tests;

public sealed class ApiTokenAuthMiddlewareTests
{
    [Fact]
    public async Task AdminApi_WhenDisabled_ReturnsNotFoundWithoutCallingNext()
    {
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:Security:EnableAdminApi"] = "false"
            })
            .Build();
        var services = new ServiceCollection().BuildServiceProvider();
        var middleware = new ApiTokenAuthMiddleware(
            next,
            configuration,
            services.GetRequiredService<IServiceScopeFactory>());
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/admin/customers";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(nextCalled);
    }
}
