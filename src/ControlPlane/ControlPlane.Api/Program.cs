using ControlPlane.Api.Data;
using ControlPlane.Api.Email;
using ControlPlane.Api.Seed;
using ControlPlane.Api.Security;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);
var requireHttps = builder.Configuration.GetValue("ControlPlane:Security:RequireHttps", false);
var useForwardedHeaders = builder.Configuration.GetValue("ControlPlane:Security:UseForwardedHeaders", false);

builder.WebHost.ConfigureKestrel(options =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(15);
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
});

if (!builder.Environment.IsDevelopment())
{
    if (OperatingSystem.IsWindows() && !Environment.UserInteractive)
    {
        builder.Host.UseWindowsService(options => options.ServiceName = "TRAT ControlPlane");
    }
}

builder.Services.AddControllersWithViews();
builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = requireHttps ? "__Host-TRAT.Antiforgery" : "TRAT.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<AwsDiscoveryService>();
builder.Services.AddSingleton<HostOperationalStatusService>();
builder.Services.AddSingleton<AgentPackageCatalogService>();
builder.Services.AddScoped<AuditTrailService>();
builder.Services.AddScoped<OperationalAlertService>();
builder.Services.AddScoped<PanelPasswordHasher>();
builder.Services.AddScoped<PanelAuthenticationService>();
builder.Services.AddScoped<PanelBootstrapService>();
builder.Services.AddScoped<PolicyResolutionService>();
builder.Services.AddHostedService<OperationalAlertReconciliationHostedService>();
builder.Services.AddHostedService<SqliteBackupHostedService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.Value ?? string.Empty;
        var category = path.StartsWith("/login", StringComparison.OrdinalIgnoreCase)
            ? "login"
            : path.StartsWith("/api/v1/agents", StringComparison.OrdinalIgnoreCase)
                ? "agent"
                : path.StartsWith("/api/v1/admin", StringComparison.OrdinalIgnoreCase)
                    ? "admin-api"
                    : "general";
        var permitLimit = category switch
        {
            "login" => 10,
            "agent" => 300,
            "admin-api" => 60,
            _ => 600
        };
        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{category}:{remoteIp}",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = permitLimit,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});

if (useForwardedHeaders)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        options.ForwardLimit = 1;
        options.KnownProxies.Add(IPAddress.Loopback);
        options.KnownProxies.Add(IPAddress.IPv6Loopback);

        foreach (var configuredProxy in builder.Configuration.GetSection("ControlPlane:Security:TrustedProxies").Get<string[]>() ?? [])
        {
            if (IPAddress.TryParse(configuredProxy, out var proxyAddress))
            {
                options.KnownProxies.Add(proxyAddress);
            }
        }

        foreach (var configuredNetwork in builder.Configuration.GetSection("ControlPlane:Security:TrustedNetworks").Get<string[]>() ?? [])
        {
            var parts = configuredNetwork.Split('/');
            if (parts.Length == 2 &&
                IPAddress.TryParse(parts[0], out var networkAddress) &&
                int.TryParse(parts[1], out var prefixLength))
            {
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(networkAddress, prefixLength));
            }
        }
    });
}

builder.Services.AddSession(options =>
{
    options.Cookie.Name = requireHttps ? "__Host-TRAT.Session" : "TRAT.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = requireHttps ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

var configuredSqlitePath = builder.Configuration["ControlPlane:Database:SqlitePath"] ?? "data/controlplane.db";
var sqlitePath = Path.IsPathRooted(configuredSqlitePath)
    ? configuredSqlitePath
    : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configuredSqlitePath));
Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath) ?? Path.Combine(builder.Environment.ContentRootPath, "data"));

var dataDir = Path.GetDirectoryName(sqlitePath) ?? Path.Combine(builder.Environment.ContentRootPath, "data");
var keysDir = Path.Combine(dataDir, "keys");
Directory.CreateDirectory(keysDir);
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysDir))
    .SetApplicationName("TRAT.ControlPlane");
if (OperatingSystem.IsWindows() && builder.Configuration.GetValue("ControlPlane:Security:ProtectDataProtectionKeysWithDpapi", true))
{
    dataProtection.ProtectKeysWithDpapi();
}

var csb = new SqliteConnectionStringBuilder
{
    DataSource = sqlitePath,
    ForeignKeys = true
};

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(csb.ToString()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
    await SchemaBootstrapper.EnsureExtendedSchemaAsync(db);
    var panelBootstrap = scope.ServiceProvider.GetRequiredService<PanelBootstrapService>();
    await panelBootstrap.EnsureBootstrapAdminAsync(CancellationToken.None);
    var enableDemoData = builder.Configuration.GetValue<bool>("ControlPlane:Seed:EnableDemoData");
    if (enableDemoData)
    {
        await DemoDataSeeder.SeedAsync(db);
    }
}

if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync("Ocorreu um erro interno. Consulte os logs do ControlPlane.");
    }));
}

if (requireHttps)
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        headers["Content-Security-Policy"] = "default-src 'self'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'; img-src 'self' data:; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'";
        if (context.Request.Path.StartsWithSegments("/admin") ||
            context.Request.Path.StartsWithSegments("/login") ||
            context.Request.Path.StartsWithSegments("/api"))
        {
            headers.CacheControl = "no-store, no-cache, must-revalidate";
            headers.Pragma = "no-cache";
        }
        return Task.CompletedTask;
    });

    await next();
});

app.UseRateLimiter();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public,max-age=3600"
});
app.UseSession();
app.UseMiddleware<ApiTokenAuthMiddleware>();
app.MapControllers();
app.MapDefaultControllerRoute();

app.Run();
