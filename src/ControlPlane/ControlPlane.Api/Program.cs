using ControlPlane.Api.Data;
using ControlPlane.Api.Email;
using ControlPlane.Api.Seed;
using ControlPlane.Api.Security;
using ControlPlane.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSingleton<SmtpEmailSender>();
builder.Services.AddSingleton<AwsDiscoveryService>();
builder.Services.AddSingleton<HostOperationalStatusService>();
builder.Services.AddScoped<PolicyResolutionService>();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

var configuredSqlitePath = builder.Configuration["ControlPlane:Database:SqlitePath"] ?? "data/controlplane.db";
var sqlitePath = Path.IsPathRooted(configuredSqlitePath)
    ? configuredSqlitePath
    : Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, configuredSqlitePath));
Directory.CreateDirectory(Path.GetDirectoryName(sqlitePath) ?? Path.Combine(builder.Environment.ContentRootPath, "data"));

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
