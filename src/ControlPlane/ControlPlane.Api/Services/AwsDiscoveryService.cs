using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;

namespace ControlPlane.Api.Services;

public sealed record AwsBucketDescriptor(string Name);

public sealed record AwsDiscoveryResult(
    bool IsConnected,
    string? ExpectedAccountId,
    string? ResolvedAccountId,
    string? SelectedBucket,
    string? SelectedBucketRegion,
    string Message,
    IReadOnlyList<AwsBucketDescriptor> Buckets,
    IReadOnlyList<string> Prefixes);

public sealed class AwsDiscoveryService(ILogger<AwsDiscoveryService> logger)
{
    public async Task<AwsDiscoveryResult> DiscoverAsync(string? expectedAccountId, string? selectedBucket, CancellationToken ct)
    {
        try
        {
            var credentials = FallbackCredentialsFactory.GetCredentials();
            var region = RegionEndpoint.USEast1;
            using var sts = new AmazonSecurityTokenServiceClient(credentials, region);
            var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), ct);

            var normalizedExpectedAccountId = Normalize(expectedAccountId);
            if (!string.IsNullOrWhiteSpace(normalizedExpectedAccountId) &&
                !string.Equals(identity.Account, normalizedExpectedAccountId, StringComparison.Ordinal))
            {
                return new AwsDiscoveryResult(
                    IsConnected: false,
                    ExpectedAccountId: normalizedExpectedAccountId,
                    ResolvedAccountId: identity.Account,
                    SelectedBucket: Normalize(selectedBucket),
                    SelectedBucketRegion: null,
                    Message: $"Conta AWS divergente. Esperado={normalizedExpectedAccountId}. Atual={identity.Account}.",
                    Buckets: Array.Empty<AwsBucketDescriptor>(),
                    Prefixes: Array.Empty<string>());
            }

            using var s3 = new AmazonS3Client(credentials, region);
            var bucketsResponse = await s3.ListBucketsAsync(ct);
            var buckets = bucketsResponse.Buckets
                .OrderBy(b => b.BucketName, StringComparer.OrdinalIgnoreCase)
                .Select(b => new AwsBucketDescriptor(b.BucketName))
                .ToArray();

            var normalizedSelectedBucket = Normalize(selectedBucket);
            if (string.IsNullOrWhiteSpace(normalizedSelectedBucket) && buckets.Length > 0)
            {
                normalizedSelectedBucket = buckets[0].Name;
            }

            string? selectedBucketRegion = null;
            IReadOnlyList<string> prefixes = Array.Empty<string>();
            if (!string.IsNullOrWhiteSpace(normalizedSelectedBucket))
            {
                selectedBucketRegion = await GetBucketRegionAsync(s3, normalizedSelectedBucket, region.SystemName, ct);
                prefixes = await ListPrefixesAsync(s3, normalizedSelectedBucket, ct);
            }

            var message = buckets.Length == 0
                ? $"Conta AWS validada ({identity.Account}), mas nenhum bucket visivel foi encontrado."
                : $"Conta AWS validada ({identity.Account}) e {buckets.Length} bucket(s) encontrado(s).";

            return new AwsDiscoveryResult(
                IsConnected: true,
                ExpectedAccountId: normalizedExpectedAccountId,
                ResolvedAccountId: identity.Account,
                SelectedBucket: normalizedSelectedBucket,
                SelectedBucketRegion: selectedBucketRegion,
                Message: message,
                Buckets: buckets,
                Prefixes: prefixes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "AWS discovery failed for expected account {ExpectedAccountId}", expectedAccountId);
            return new AwsDiscoveryResult(
                IsConnected: false,
                ExpectedAccountId: Normalize(expectedAccountId),
                ResolvedAccountId: null,
                SelectedBucket: Normalize(selectedBucket),
                SelectedBucketRegion: null,
                Message: "Falha ao validar AWS no ControlPlane. Verifique credenciais locais, permissao STS e acesso S3.",
                Buckets: Array.Empty<AwsBucketDescriptor>(),
                Prefixes: Array.Empty<string>());
        }
    }

    private static async Task<IReadOnlyList<string>> ListPrefixesAsync(IAmazonS3 s3, string bucketName, CancellationToken ct)
    {
        var response = await s3.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = bucketName,
            Delimiter = "/",
            MaxKeys = 200
        }, ct);

        return response.CommonPrefixes
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(prefix => prefix.Trim())
            .OrderBy(prefix => prefix, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static async Task<string> GetBucketRegionAsync(IAmazonS3 s3, string bucketName, string fallbackRegion, CancellationToken ct)
    {
        var response = await s3.GetBucketLocationAsync(new GetBucketLocationRequest
        {
            BucketName = bucketName
        }, ct);

        var region = response.Location?.Value;
        if (string.IsNullOrWhiteSpace(region))
        {
            return fallbackRegion;
        }

        if (string.Equals(region, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return "eu-west-1";
        }

        return region.Trim();
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
