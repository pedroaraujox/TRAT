using Microsoft.Data.Sqlite;

namespace ControlPlane.Api.Services;

public sealed class SqliteBackupHostedService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<SqliteBackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("ControlPlane:Backup:Enabled", false))
        {
            return;
        }

        var initialDelaySeconds = Math.Max(5, configuration.GetValue("ControlPlane:Backup:InitialDelaySeconds", 300));
        await Task.Delay(TimeSpan.FromSeconds(initialDelaySeconds), stoppingToken);

        var intervalHours = Math.Max(1, configuration.GetValue("ControlPlane:Backup:IntervalHours", 24));
        while (!stoppingToken.IsCancellationRequested)
        {
            await CreateBackupAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
        }
    }

    private async Task CreateBackupAsync(CancellationToken ct)
    {
        try
        {
            var sourcePath = ResolvePath(configuration["ControlPlane:Database:SqlitePath"] ?? "data/controlplane.db");
            if (!File.Exists(sourcePath))
            {
                logger.LogWarning("Backup SQLite ignorado porque o banco nao existe em {DatabasePath}", sourcePath);
                return;
            }

            var backupDirectory = ResolvePath(configuration["ControlPlane:Backup:Directory"] ?? "backups");
            Directory.CreateDirectory(backupDirectory);
            var destinationPath = Path.Combine(backupDirectory, $"controlplane-{DateTime.UtcNow:yyyyMMdd-HHmmss}.db");

            var sourceBuilder = new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly };
            var destinationBuilder = new SqliteConnectionStringBuilder { DataSource = destinationPath };
            await using var source = new SqliteConnection(sourceBuilder.ToString());
            await using var destination = new SqliteConnection(destinationBuilder.ToString());
            await source.OpenAsync(ct);
            await destination.OpenAsync(ct);
            source.BackupDatabase(destination);

            var retentionDays = Math.Max(1, configuration.GetValue("ControlPlane:Backup:RetentionDays", 30));
            var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);
            foreach (var oldBackup in Directory.GetFiles(backupDirectory, "controlplane-*.db"))
            {
                if (File.GetLastWriteTimeUtc(oldBackup) < cutoffUtc)
                {
                    File.Delete(oldBackup);
                }
            }

            logger.LogInformation("Backup SQLite consistente criado em {BackupPath}", destinationPath);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao criar backup SQLite automatico.");
        }
    }

    private string ResolvePath(string configuredPath)
        => Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(environment.ContentRootPath, configuredPath));
}
