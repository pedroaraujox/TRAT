using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using WebstationBackup.Agent.Service.Core;
using WebstationBackup.Agent.Service.Logging;

namespace WebstationBackup.Agent.Service.Aws;

internal sealed class AwsReadinessCheckResult
{
    public required bool CredentialOk { get; init; }
    public required string Message { get; init; }
    public string? CredentialSource { get; init; }
    public string? AwsAccountId { get; init; }
    public string? BucketRegion { get; init; }
    public bool? BucketDiscoveryOk { get; init; }
    public string? BucketDiscoveryMessage { get; init; }
    public IReadOnlyList<string> AvailableBuckets { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, string> AvailableBucketRegions { get; init; } = new Dictionary<string, string>();
}

internal sealed class AwsReadinessValidator
{
    public async Task<AwsReadinessCheckResult> ValidateAsync(AgentSettings settings, ILogger logger, CancellationToken ct)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        if (logger is null)
        {
            throw new ArgumentNullException(nameof(logger));
        }

        ResolvedAwsCredentials resolved;
        try
        {
            resolved = AwsCredentialResolver.ResolveOrThrow(settings.AwsCredentialTargetName, settings.AwsCredentialDpapiProtected);
        }
        catch (Exception ex)
        {
            logger.Warn("Falha ao resolver credenciais AWS", new Dictionary<string, object?>
            {
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message
            });
            return Failed("Nenhuma credencial AWS valida foi encontrada no host.");
        }

        var awsRegion = settings.AwsRegion;
        var s3BucketName = settings.S3BucketName;
        var regionName = (awsRegion ?? string.Empty).Trim();
        if (regionName.Length == 0)
        {
            regionName = "us-east-1";
        }

        var bucketNameTrimmed = (s3BucketName ?? string.Empty).Trim();
        var bucketName = bucketNameTrimmed.Length == 0 ? null : bucketNameTrimmed;
        var prefix = NormalizePrefix(settings.S3KeyPrefix ?? string.Empty);

        try
        {
            var region = RegionEndpoint.GetBySystemName(regionName);
            using var sts = new AmazonSecurityTokenServiceClient(resolved.Credentials, region);
            var identity = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest(), ct);
            IReadOnlyList<string> availableBuckets = Array.Empty<string>();
            IReadOnlyDictionary<string, string> availableBucketRegions = new Dictionary<string, string>();
            var bucketDiscoveryOk = false;
            var bucketDiscoveryMessage = "A descoberta de buckets AWS ainda nao foi concluida.";
            try
            {
                using var discoveryS3 = new AmazonS3Client(resolved.Credentials, region);
                var bucketsResponse = await discoveryS3.ListBucketsAsync(ct);
                availableBuckets = bucketsResponse.Buckets
                    .Where(bucket => !string.IsNullOrWhiteSpace(bucket.BucketName))
                    .Select(bucket => bucket.BucketName.Trim())
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var regions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var availableBucket in availableBuckets)
                {
                    try
                    {
                        var location = await discoveryS3.GetBucketLocationAsync(new GetBucketLocationRequest
                        {
                            BucketName = availableBucket
                        }, ct);
                        regions[availableBucket] = NormalizeRegion(location.Location?.Value, "us-east-1");
                    }
                    catch (AmazonS3Exception ex)
                    {
                        logger.Warn("Nao foi possivel descobrir a regiao de um bucket AWS.", new Dictionary<string, object?>
                        {
                            ["bucket"] = availableBucket,
                            ["statusCode"] = (int)ex.StatusCode
                        });
                    }
                }

                availableBucketRegions = regions;
                bucketDiscoveryOk = true;
                bucketDiscoveryMessage = availableBuckets.Count == 0
                    ? "A credencial AWS pode listar buckets, mas a conta nao retornou buckets visiveis."
                    : $"A credencial AWS reportou {availableBuckets.Count} bucket(s) visivel(is).";
            }
            catch (AmazonS3Exception ex)
            {
                bucketDiscoveryMessage = ex.StatusCode == HttpStatusCode.Forbidden || ex.StatusCode == HttpStatusCode.Unauthorized
                    ? "A AWS negou a listagem de buckets. Confirme a permissao s3:ListAllMyBuckets."
                    : $"A AWS nao concluiu a listagem de buckets (HTTP {(int)ex.StatusCode}).";
                logger.Warn("Nao foi possivel listar os buckets visiveis para a credencial do Agent.", new Dictionary<string, object?>
                {
                    ["statusCode"] = (int)ex.StatusCode,
                    ["credentialSource"] = resolved.Source
                });
            }

            if (string.IsNullOrWhiteSpace(bucketName))
            {
                logger.Info("AWS readiness validado (identidade apenas)", new Dictionary<string, object?>
                {
                    ["credentialSource"] = resolved.Source,
                    ["credentialReference"] = resolved.Reference,
                    ["awsAccountId"] = identity.Account
                });

                return new AwsReadinessCheckResult
                {
                    CredentialOk = true,
                    CredentialSource = resolved.Source,
                    AwsAccountId = identity.Account,
                    BucketRegion = null,
                    BucketDiscoveryOk = bucketDiscoveryOk,
                    BucketDiscoveryMessage = bucketDiscoveryMessage,
                    AvailableBuckets = availableBuckets,
                    AvailableBucketRegions = availableBucketRegions,
                    Message = $"AWS validado (identidade). Conta={identity.Account}. FonteCredencial={resolved.Source}."
                };
            }

            using var s3 = new AmazonS3Client(resolved.Credentials, region);
            var bucketLocation = await s3.GetBucketLocationAsync(new GetBucketLocationRequest
            {
                BucketName = bucketName
            }, ct);

            var effectiveBucketRegion = NormalizeRegion(bucketLocation.Location?.Value, fallbackRegion: region.SystemName);
            if (!string.Equals(effectiveBucketRegion, region.SystemName, StringComparison.OrdinalIgnoreCase))
            {
                return Failed(
                    "Bucket AWS acessivel, mas em regiao diferente da configurada no host.",
                    resolved.Source,
                    identity.Account,
                    effectiveBucketRegion);
            }

            await s3.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = bucketName,
                Prefix = prefix,
                MaxKeys = 1
            }, ct);

            logger.Info("AWS readiness validado em modo somente leitura", new Dictionary<string, object?>
            {
                ["credentialSource"] = resolved.Source,
                ["credentialReference"] = resolved.Reference,
                ["awsAccountId"] = identity.Account,
                ["bucket"] = bucketName,
                ["bucketRegion"] = effectiveBucketRegion,
                ["prefix"] = prefix
            });

            return new AwsReadinessCheckResult
            {
                CredentialOk = true,
                CredentialSource = resolved.Source,
                AwsAccountId = identity.Account,
                BucketRegion = effectiveBucketRegion,
                BucketDiscoveryOk = bucketDiscoveryOk,
                BucketDiscoveryMessage = bucketDiscoveryMessage,
                AvailableBuckets = availableBuckets,
                AvailableBucketRegions = availableBucketRegions,
                Message = $"AWS validado em modo leitura. Conta={identity.Account}. Bucket={bucketName}. Regiao={effectiveBucketRegion}. Prefixo={prefix}. FonteCredencial={resolved.Source}."
            };
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden || ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.Warn("Acesso negado ao bucket AWS durante readiness", new Dictionary<string, object?>
            {
                ["bucket"] = bucketName,
                ["statusCode"] = (int)ex.StatusCode,
                ["credentialSource"] = resolved.Source
            });

            return Failed("Acesso negado ao bucket AWS configurado no host.", resolved.Source);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Failed("Bucket AWS configurado no host nao foi encontrado.", resolved.Source);
        }
        catch (AmazonSecurityTokenServiceException ex)
        {
            logger.Warn("Falha STS durante readiness AWS", new Dictionary<string, object?>
            {
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message,
                ["credentialSource"] = resolved.Source
            });

            return Failed("Falha ao validar identidade AWS da credencial local.", resolved.Source);
        }
        catch (Exception ex)
        {
            logger.Warn("Falha inesperada no readiness AWS", new Dictionary<string, object?>
            {
                ["exceptionType"] = ex.GetType().FullName,
                ["message"] = ex.Message,
                ["credentialSource"] = resolved.Source
            });

            return Failed("Falha ao validar configuracoes AWS do host em modo leitura.", resolved.Source);
        }
    }

    private static AwsReadinessCheckResult Failed(string message, string? credentialSource = null, string? awsAccountId = null, string? bucketRegion = null)
    {
        return new AwsReadinessCheckResult
        {
            CredentialOk = false,
            Message = message,
            CredentialSource = credentialSource,
            AwsAccountId = awsAccountId,
            BucketRegion = bucketRegion
        };
    }

    private static string NormalizePrefix(string value)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('/');
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed + "/";
    }

    private static string NormalizeRegion(string? value, string fallbackRegion)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallbackRegion;
        }

        if (string.Equals(value, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return "eu-west-1";
        }

        return value!.Trim();
    }
}
