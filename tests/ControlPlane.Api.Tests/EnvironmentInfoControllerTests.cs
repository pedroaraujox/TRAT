using ControlPlane.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace ControlPlane.Api.Tests;

public sealed class EnvironmentInfoControllerTests
{
    [Fact]
    public void GetEnvironment_ReturnsConfiguredEnvironmentUrlAndRevision()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ControlPlane:Environment:Name"] = "hml",
                ["ControlPlane:Environment:PublicUrl"] = "https://trat-hml.outboxtech.com.br",
                ["ControlPlane:Environment:Revision"] = "abc123"
            })
            .Build();

        var controller = new EnvironmentInfoController(configuration);

        var result = Assert.IsType<OkObjectResult>(controller.GetEnvironment());
        var payload = result.Value!;
        Assert.Equal("hml", ReadProperty(payload, "name"));
        Assert.Equal("https://trat-hml.outboxtech.com.br", ReadProperty(payload, "publicUrl"));
        Assert.Equal("abc123", ReadProperty(payload, "revision"));
    }

    private static string? ReadProperty(object payload, string name)
        => payload.GetType().GetProperty(name)?.GetValue(payload)?.ToString();
}
