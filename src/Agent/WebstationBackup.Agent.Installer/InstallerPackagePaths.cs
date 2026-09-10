using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace WebstationBackup.Agent.Installer;

internal sealed record PackageLayout(
    string PackageRoot,
    string InstallScriptPath,
    string ServiceExecutablePath,
    string TrayExecutablePath,
    string SettingsTemplatePath,
    string RulesPath);

internal sealed record InstallerPackageManifest(string? Environment, string? ControlPlaneBaseUrl, string? GeneratedAtUtc, string? Revision);

internal static class InstallerPackagePaths
{
    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "TRAT",
        "Agent");

    public static string DefaultStateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "TRAT",
        "Agent");

    public static string DefaultSettingsOutputPath(string packageRoot) => Path.Combine(packageRoot, "agent.settings.json");

    public static string ReadDefaultControlPlaneUrl(string packageRoot)
    {
        try
        {
            var path = Path.Combine(packageRoot, "controlplane.url");
            if (File.Exists(path))
            {
                var configured = File.ReadAllText(path).Trim().TrimEnd('/');
                if (Uri.TryCreate(configured, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttps || IsLoopbackHttp(uri)))
                {
                    return configured;
                }
            }
        }
        catch
        {
        }

        return "https://trat-hml.outboxtech.com.br";
    }

    public static InstallerPackageManifest? ReadManifest(string packageRoot)
    {
        try
        {
            var path = Path.Combine(packageRoot, "agent-package.manifest.json");
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<InstallerPackageManifest>(File.ReadAllText(path), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLoopbackHttp(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback;

    private const string EmbeddedPayloadResourceName = "AgentPackagePayload.zip";

    public static string ResolveInitialPackageRoot()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var candidate = FindPackageRoot(baseDirectory);
        if (candidate is not null)
        {
            return candidate;
        }

        var extracted = TryExtractEmbeddedPayload();
        if (extracted is not null)
        {
            return extracted;
        }

        return baseDirectory;
    }

    private static string? TryExtractEmbeddedPayload()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var resourceStream = assembly.GetManifestResourceStream(EmbeddedPayloadResourceName);
            if (resourceStream is null)
            {
                return null;
            }

            var targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TRAT",
                "AgentSetupPayload");

            var markerPath = Path.Combine(targetDir, ".payload-sha256");
            var expectedMarker = ComputeSha256(resourceStream);
            resourceStream.Position = 0;
            if (Directory.Exists(targetDir) &&
                File.Exists(markerPath) &&
                File.ReadAllText(markerPath).Trim() == expectedMarker &&
                LooksLikePackageRoot(targetDir))
            {
                return targetDir;
            }

            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }
            Directory.CreateDirectory(targetDir);

            using (var archive = new ZipArchive(resourceStream, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(targetDir, overwriteFiles: true);
            }

            File.WriteAllText(markerPath, expectedMarker);

            return LooksLikePackageRoot(targetDir) ? targetDir : null;
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256(Stream input)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(input);
        return Convert.ToBase64String(hash);
    }

    public static PackageLayout Resolve(string packageRoot)
    {
        var normalizedRoot = Path.GetFullPath(packageRoot.Trim());
        return new PackageLayout(
            PackageRoot: normalizedRoot,
            InstallScriptPath: Path.Combine(normalizedRoot, "Install-Agent.ps1"),
            ServiceExecutablePath: Path.Combine(normalizedRoot, "bin", "WebstationBackup.Agent.Service.exe"),
            TrayExecutablePath: Path.Combine(normalizedRoot, "tray", "WebstationBackup.Agent.Tray.exe"),
            SettingsTemplatePath: Path.Combine(normalizedRoot, "agent.settings.template.json"),
            RulesPath: Path.Combine(normalizedRoot, "project.rules.json"));
    }

    private static string? FindPackageRoot(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        for (var i = 0; i < 6 && current is not null; i++)
        {
            var candidate = current.FullName;
            if (LooksLikePackageRoot(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool LooksLikePackageRoot(string candidate)
    {
        var layout = Resolve(candidate);
        return File.Exists(layout.InstallScriptPath)
            && File.Exists(layout.ServiceExecutablePath)
            && File.Exists(layout.SettingsTemplatePath)
            && File.Exists(layout.RulesPath);
    }
}
