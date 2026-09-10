using System;
using System.IO;
using WebstationBackup.Agent.Service.Backup;
using Xunit;

namespace Agent.Tests;

public sealed class ManifestPathTests
{
    [Fact]
    public void RetainsNestedPathFromRemotePolicy()
    {
        var root = Path.Combine(Path.GetTempPath(), "policy");
        Assert.Equal("nested/file.txt", ManifestBuilder.MakeRelativePathForKey(new[] { root }, Path.Combine(root, "nested", "file.txt")));
    }

    [Fact]
    public void DifferentRootsCannotOverwriteSameRelativeName()
    {
        var a = Path.Combine(Path.GetTempPath(), "root-a");
        var b = Path.Combine(Path.GetTempPath(), "root-b");
        var roots = new[] { a, b };
        Assert.NotEqual(ManifestBuilder.MakeRelativePathForKey(roots, Path.Combine(a, "file.txt")),
            ManifestBuilder.MakeRelativePathForKey(roots, Path.Combine(b, "file.txt")));
    }

    [Fact]
    public void OutsideRootFailsClosed()
    {
        Assert.Throws<InvalidOperationException>(() => ManifestBuilder.MakeRelativePathForKey(new[] { @"C:\authorized" }, @"C:\other\file.txt"));
    }
}
