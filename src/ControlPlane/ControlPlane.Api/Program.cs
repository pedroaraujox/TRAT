using ControlPlane.Api.Data;
using ControlPlane.Api.Email;
using ControlPlane.Api.Seed;
using ControlPlane.Api.Security;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

if (!builder.Environment.IsDevelopment())
{
    builder.Host.UseContentRoot(AppContext.BaseDirectory);
    if (OperatingSystem.IsWindows())
    {
        builder.Host.UseWindowsService(options => options.ServiceName = "TRAT ControlPlane");
    }
}

builder.Services.AddControllersWithViews();
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
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.Cookie.Name = "TRAT.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
if (OperatingSystem.IsWindows())
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

app.UseStaticFiles();
app.UseSession();
app.UseMiddleware<ApiTokenAuthMiddleware>();
app.MapControllers();
app.MapDefaultControllerRoute();

app.Run();
