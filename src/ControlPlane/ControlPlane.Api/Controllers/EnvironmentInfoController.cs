using Microsoft.AspNetCore.Mvc;

namespace ControlPlane.Api.Controllers;

[ApiController]
public sealed class EnvironmentInfoController(IConfiguration configuration) : ControllerBase
{
    [HttpGet("/api/v1/environment")]
    public IActionResult GetEnvironment()
    {
        return Ok(new
        {
            name = configuration["ControlPlane:Environment:Name"] ?? "unconfigured",
            publicUrl = configuration["ControlPlane:Environment:PublicUrl"] ?? string.Empty,
            revision = configuration["ControlPlane:Environment:Revision"] ?? "unknown"
        });
    }
}
