using System.Diagnostics;

namespace ControlPlane.Api.Services;

public sealed class AgentPackageCatalogService(IWebHostEnvironment env, IConfiguration config)
{
    private static readonly string[] SetupFileNames =
    {
        "TRAT.Agent.Setup.exe",
        "WebstationBackup.Agent.Setup.exe"
    };

    private static readonly string[] ZipFileNames =
    {
        "TRAT.Agent.Package.zip",
        "WebstationBackup.Agent.Package.zip"
    };

    public AgentPackageCatalogResult GetLatestPackage()
    {
        var root = ResolveArtifactsRoot();
        if (!Directory.Exists(root))
        {
            return AgentPackageCatalogResult.NotAvailable(root, "Diretorio de artifacts do Agent nao encontrado.");
        }

        var zipFiles = ZipFileNames
            .SelectMany(fileName => Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToArray();
        var setupFiles = SetupFileNames
            .SelectMany(fileName => Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToArray();

        var latestSetup = setupFiles.FirstOrDefault();
        var latestZip = zipFiles.FirstOrDefault();
        if (latestSetup is null && latestZip is null)
        {
            return AgentPackageCatalogResult.NotAvailable(root, "Nenhum pacote do Agent foi encontrado para download.");
        }

        var referenceFile = latestSetup ?? latestZip!;
        var version = TryReadProductVersion(latestSetup?.FullName) ?? "indefinida";

        return new AgentPackageCatalogResult(
            IsAvailable: true,
            SearchRoot: root,
            Message: "Pacote do Agent localizado com sucesso.",
            Version: version,
            PublishedAtUtc: referenceFile.LastWriteTimeUtc,
            SetupExePath: latestSetup?.FullName,
            ZipPath: latestZip?.FullName);
    }

    private string ResolveArtifactsRoot()
    {
        var configured = config["ControlPlane:AgentDownloads:ArtifactsRoot"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured.Trim()
                : Path.GetFullPath(Path.Combine(env.ContentRootPath, configured.Trim()));
        }

        return Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "..", "artifacts"));
    }

    private static string? TryReadProductVersion(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var version = FileVersionInfo.GetVersionInfo(filePath).ProductVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch
        {
            return null;
        }
    }
}

public sealed record AgentPackageCatalogResult(
    bool IsAvailable,
    string SearchRoot,
    string Message,
    string? Version,
    DateTimeOffset? PublishedAtUtc,
    string? SetupExePath,
    string? ZipPath)
{
    public static AgentPackageCatalogResult NotAvailable(string searchRoot, string message)
        => new(false, searchRoot, message, null, null, null, null);
}
