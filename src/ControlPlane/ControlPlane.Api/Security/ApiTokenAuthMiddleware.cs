using Microsoft.AspNetCore.Http;
using ControlPlane.Api.Data;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace ControlPlane.Api.Security;

public sealed class ApiTokenAuthMiddleware(RequestDelegate next, IConfiguration config, IServiceScopeFactory scopeFactory)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/api/v1/health", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/v1/environment", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        if (path.StartsWith("/api/v1/agents", StringComparison.OrdinalIgnoreCase))
        {
            var provided = ctx.Request.Headers["X-Agent-Token"].ToString();
            if (string.IsNullOrWhiteSpace(provided))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Unauthorized");
                return;
            }

            var tokenHash = ComputeTokenHash(provided);
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var customerId = await db.Customers.AsNoTracking()
                .Where(c => c.AgentEnrollmentTokenHash == tokenHash)
                .Select(c => c.Id)
                .FirstOrDefaultAsync(ctx.RequestAborted);
            if (string.IsNullOrWhiteSpace(customerId))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await ctx.Response.WriteAsync("Unauthorized");
                return;
            }

            ctx.Items["AgentCustomerId"] = customerId;
        }

        if (path.StartsWith("/api/v1/admin", StringComparison.OrdinalIgnoreCase))
        {
            if (!config.GetValue("ControlPlane:Security:EnableAdminApi", false))
            {
                ctx.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

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
            var userId = ctx.Session.GetString(PanelSecurityConstants.SessionUserId);
            if (string.IsNullOrWhiteSpace(userId))
            {
                ctx.Response.Redirect("/login");
                return;
            }
        }

        if (path.StartsWith("/login", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(ctx.Session.GetString(PanelSecurityConstants.SessionUserId)))
        {
            ctx.Response.Redirect("/admin");
            return;
        }

        if (path.StartsWith("/admin/panel", StringComparison.OrdinalIgnoreCase))
        {
            var role = ctx.Session.GetString(PanelSecurityConstants.SessionUserRole);
            if (!PanelSecurityConstants.IsAdminRole(role))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                await ctx.Response.WriteAsync("Forbidden");
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

    private static string ComputeTokenHash(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(token.Trim());
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }
}
