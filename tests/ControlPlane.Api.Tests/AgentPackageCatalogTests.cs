using System.Security.Cryptography;
using System.Text.Json;
using ControlPlane.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace ControlPlane.Api.Tests;

public sealed class AgentPackageCatalogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "trat-catalog-" + Guid.NewGuid().ToString("N"));
    private const string Revision = "0123456789012345678901234567890123456789";

    [Fact]
    public void SelectsMatchingReleaseEvenWhenAnotherEnvironmentIsNewer()
    {
        var expected = Package("hml", Revision, "hml");
        Package("production", Revision, "production", 1);
        var result = Catalog().GetLatestPackage();
        Assert.True(result.IsAvailable);
        Assert.Equal(expected, result.SetupExePath);
        Assert.Equal("1.0.0+012345678901", result.Version);
    }

    [Fact]
    public void RejectsPreviousRevision()
    {
        Package("old", "previous", "hml");
        Assert.False(Catalog().GetLatestPackage().IsAvailable);
    }

    [Fact]
    public void RejectsTamperedSetup()
    {
        var setup = Package("hml", Revision, "hml");
        File.AppendAllText(setup, "tampered");
        Assert.False(Catalog().GetLatestPackage().IsAvailable);
    }

    [Fact]
    public void DoesNotAttachUnrelatedZip()
    {
        Package("hml", Revision, "hml");
        Directory.CreateDirectory(Path.Combine(root, "other"));
        File.WriteAllText(Path.Combine(root, "other", "TRAT.Agent.Package.zip"), "other release");
        Assert.Null(Catalog().GetLatestPackage().ZipPath);
    }

    [Fact]
    public void InvalidManifestFailsClosed()
    {
        var setup = Package("hml", Revision, "hml");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(setup)!, "agent-package.manifest.json"), "{");
        Assert.False(Catalog().GetLatestPackage().IsAvailable);
    }

    private string Package(string directory, string revision, string environment, int days = 0)
    {
        var path = Path.Combine(root, directory);
        Directory.CreateDirectory(path);
        var setup = Path.Combine(path, "TRAT.Agent.Setup.exe");
        File.WriteAllText(setup, "synthetic fixture " + directory);
        File.WriteAllText(Path.Combine(path, "agent-package.manifest.json"), JsonSerializer.Serialize(new {
            environment, revision, version = "1.0.0+012345678901", generatedAtUtc = DateTimeOffset.UtcNow.AddDays(days),
            controlPlaneBaseUrl = environment == "hml" ? "https://trat-hml.outboxtech.com.br" : "https://trat.outboxtech.com.br",
            setupSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(setup)))
        }));
        return setup;
    }

    private AgentPackageCatalogService Catalog() => new(new TestEnvironment(), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["ControlPlane:AgentDownloads:ArtifactsRoot"] = root,
        ["ControlPlane:Environment:Name"] = "hml",
        ["ControlPlane:Environment:PublicUrl"] = "https://trat-hml.outboxtech.com.br",
        ["ControlPlane:Environment:Revision"] = Revision
    }).Build());

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Test";
        public string EnvironmentName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public string WebRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
