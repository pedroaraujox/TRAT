using ControlPlane.Api.Data;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Controllers;

[ApiController]
public sealed class ReadinessController(AppDbContext db, AgentPackageCatalogService packages, IConfiguration configuration) : ControllerBase
{
    [HttpGet("/api/v1/readiness")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var databaseReady = await db.Database.CanConnectAsync(ct);
        var package = packages.GetLatestPackage();
        var ready = databaseReady && package.IsAvailable;
        return StatusCode(ready ? 200 : 503, new {
            status = ready ? "ready" : "not_ready", databaseReady, agentPackageReady = package.IsAvailable,
            agentVersion = package.Version, environment = configuration["ControlPlane:Environment:Name"],
            revision = configuration["ControlPlane:Environment:Revision"]
        });
    }
}
