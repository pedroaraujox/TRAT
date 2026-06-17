using Microsoft.AspNetCore.Http;

namespace ControlPlane.Api.Security;

public sealed class ApiTokenAuthMiddleware(RequestDelegate next, IConfiguration config)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/v1/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        if (path.StartsWith("/api/v1/agents", StringComparison.OrdinalIgnoreCase))
        {
            var expected = config["ControlPlane:Security:AgentToken"] ?? string.Empty;
            var provided = ctx.Request.Headers["X-Agent-Token"].ToString();
            if (string.IsNullOrWhiteSpace(expected) || !ConstantTimeEquals(expected, provided))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        if (path.StartsWith("/api/v1/admin", StringComparison.OrdinalIgnoreCase))
        {
            var expected = config["ControlPlane:Security:AdminToken"] ?? string.Empty;
            var provided = ctx.Request.Headers["X-Admin-Token"].ToString();
            if (string.IsNullOrWhiteSpace(expected) || !ConstantTimeEquals(expected, provided))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Unauthorized");
                return;
            }
        }

        if (path.StartsWith("/admin", StringComparison.OrdinalIgnoreCase))
        {
            var expected = config["ControlPlane:Security:AdminToken"] ?? string.Empty;
            var provided = ctx.Session.GetString("admin-token") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expected) || !ConstantTimeEquals(expected, provided))
            {
                ctx.Response.Redirect("/login");
                return;
            }
        }

        await next(ctx);
    }

    private static bool ConstantTimeEquals(string a, string b)
    {
        if (a is null || b is null)
        {
            return false;
        }

        var aBytes = System.Text.Encoding.UTF8.GetBytes(a);
        var bBytes = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}
