using System.Security.Cryptography;
using System.Text.Json;

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

        var setupFiles = SetupFileNames
            .SelectMany(fileName => Directory.GetFiles(root, fileName, SearchOption.AllDirectories))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToArray();

        var expectedEnvironment = Normalize(config["ControlPlane:Environment:Name"]);
        var expectedUrl = NormalizeUrl(config["ControlPlane:Environment:PublicUrl"]);
        var expectedRevision = Normalize(config["ControlPlane:Environment:Revision"]);
        var candidates = setupFiles.Select(file => new { File = file, Manifest = TryReadManifest(file.DirectoryName) })
            .Where(candidate => candidate.Manifest is not null &&
                !string.IsNullOrWhiteSpace(expectedEnvironment) && !string.IsNullOrWhiteSpace(expectedUrl) &&
                string.Equals(Normalize(candidate.Manifest.Environment), expectedEnvironment, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(NormalizeUrl(candidate.Manifest.ControlPlaneBaseUrl), expectedUrl, StringComparison.OrdinalIgnoreCase) &&
                (expectedEnvironment == "local" || string.Equals(candidate.Manifest.Revision, expectedRevision, StringComparison.Ordinal)))
            .OrderByDescending(candidate => candidate.Manifest!.GeneratedAtUtc)
            .ThenBy(candidate => candidate.File.FullName, StringComparer.Ordinal)
            .ToArray();
        var selected = candidates.FirstOrDefault();
        if (selected is null)
        {
            return AgentPackageCatalogResult.NotAvailable(root, "Pacote bloqueado: nenhum manifesto corresponde ao ambiente, URL e revisao do painel.");
        }
        var latestSetup = selected.File;
        var manifest = selected.Manifest!;
        if (string.IsNullOrWhiteSpace(manifest.Version) || string.IsNullOrWhiteSpace(manifest.Revision) ||
            !VerifyHash(latestSetup.FullName, manifest.SetupSha256))
        {
            return AgentPackageCatalogResult.NotAvailable(root, "Pacote bloqueado: versao, revisao ou integridade SHA-256 invalida.");
        }
        // Only offer a ZIP from the same release directory with its own verified digest.
        var latestZip = ZipFileNames.Select(name => Path.Combine(latestSetup.DirectoryName!, name))
            .FirstOrDefault(path => File.Exists(path) && VerifyHash(path, manifest.ZipSha256));

        return new AgentPackageCatalogResult(
            IsAvailable: true,
            SearchRoot: root,
            Message: "Pacote do Agent localizado com sucesso.",
            Version: manifest.Version,
            PublishedAtUtc: manifest.GeneratedAtUtc,
            SetupExePath: latestSetup?.FullName,
            ZipPath: latestZip,
            EnvironmentName: manifest.Environment,
            ControlPlaneBaseUrl: manifest.ControlPlaneBaseUrl);
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

    private static bool VerifyHash(string filePath, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected) || expected.Length != 64)
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static AgentPackageManifest? TryReadManifest(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return null;
        var path = Path.Combine(directory, "agent-package.manifest.json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<AgentPackageManifest>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? NormalizeUrl(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('/');

    private sealed record AgentPackageManifest(string Environment, string ControlPlaneBaseUrl, string Revision,
        string Version, DateTimeOffset GeneratedAtUtc, string? SetupSha256, string? ZipSha256);
}

public sealed record AgentPackageCatalogResult(
    bool IsAvailable,
    string SearchRoot,
    string Message,
    string? Version,
    DateTimeOffset? PublishedAtUtc,
    string? SetupExePath,
    string? ZipPath,
    string? EnvironmentName,
    string? ControlPlaneBaseUrl)
{
    public static AgentPackageCatalogResult NotAvailable(string searchRoot, string message)
        => new(false, searchRoot, message, null, null, null, null, null, null);
}
