using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using WebstationBackup.Agent.Service.Aws;
using WebstationBackup.Agent.Service.Logging;
using Xunit;

namespace Agent.Tests;

public sealed class S3IntegrityTests
{
    [Fact]
    public async Task ChangedFileIsRejectedBeforeAnyS3Write()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "original");
            string hash;
            using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
            File.WriteAllText(path, "modified");
            using var uploader = new S3Uploader("us-east-1", "synthetic-test", "test", new AnonymousAWSCredentials(),
                new SilentLogger(), 1, TimeSpan.Zero, TimeSpan.Zero);
            var error = await Assert.ThrowsAsync<IOException>(() => uploader.UploadFileAndVerifyAsync(path, "file", hash, CancellationToken.None));
            Assert.Contains("mudou", error.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task MissingChecksumNeverSkipsUpload()
    {
        using var uploader = new S3Uploader("us-east-1", "synthetic-test", "test", new AnonymousAWSCredentials(),
            new SilentLogger(), 1, TimeSpan.Zero, TimeSpan.Zero);
        Assert.False(await uploader.FileAlreadyExistsWithSameSha256Async("file", null, CancellationToken.None));
    }

    // Explicit integration category: run only with TRAT_TEST_AWS_BUCKET on a dedicated test bucket.
    [Fact]
    [Trait("Category", "AWS")]
    public async Task RealS3UploadVerifiesServiceChecksumAndDeduplicates()
    {
        var bucket = Environment.GetEnvironmentVariable("TRAT_TEST_AWS_BUCKET");
        Assert.False(string.IsNullOrWhiteSpace(bucket), "Set TRAT_TEST_AWS_BUCKET explicitly for this integration test.");
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "TRAT synthetic integrity test " + Guid.NewGuid().ToString("N"));
            string hash;
            using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
            using var uploader = new S3Uploader(Environment.GetEnvironmentVariable("TRAT_TEST_AWS_REGION") ?? "us-east-1",
                bucket!, "trat-validation/" + Guid.NewGuid().ToString("N"), FallbackCredentialsFactory.GetCredentials(),
                new SilentLogger(), 1, TimeSpan.Zero, TimeSpan.Zero);
            await uploader.UploadFileAndVerifyAsync(path, "synthetic.txt", hash, CancellationToken.None);
            Assert.True(await uploader.FileAlreadyExistsWithSameSha256Async("synthetic.txt", hash, CancellationToken.None));
        }
        finally { File.Delete(path); }
    }

    private sealed class SilentLogger : ILogger
    {
        public void Info(string message, IDictionary<string, object?>? props = null) { }
        public void Warn(string message, IDictionary<string, object?>? props = null) { }
        public void Error(string message, Exception ex, IDictionary<string, object?>? props = null) { }
    }
}
