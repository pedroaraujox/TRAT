using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using WebstationBackup.Agent.Service.Logging;

namespace WebstationBackup.Agent.Service.Aws;

internal sealed class S3Uploader
{
    private readonly IAmazonS3 _s3;
    private readonly ILogger _logger;
    private readonly string _bucket;
    private readonly string _keyPrefix;

    public S3Uploader(string region, string bucket, string keyPrefix, string accessKeyId, string secretAccessKey, ILogger logger)
    {
        _bucket = bucket;
        _keyPrefix = keyPrefix.Trim().TrimEnd('/') + "/";
        _logger = logger;

        var creds = new BasicAWSCredentials(accessKeyId, secretAccessKey);
        _s3 = new AmazonS3Client(creds, RegionEndpoint.GetBySystemName(region));
    }

    public async Task UploadFileAndVerifyAsync(string absolutePath, string relativePath, string? sha256Base64, CancellationToken ct)
    {
        var key = _keyPrefix + relativePath.TrimStart('/').Replace('\\', '/');

        var fi = new FileInfo(absolutePath);
        if (!fi.Exists)
        {
            throw new FileNotFoundException("Arquivo não encontrado para upload.", absolutePath);
        }

        var put = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            FilePath = absolutePath,
            AutoCloseStream = true
        };

        if (!string.IsNullOrWhiteSpace(sha256Base64))
        {
            put.Metadata.Add("sha256b64", sha256Base64);
        }

        try
        {
            var resp = await _s3.PutObjectAsync(put, ct);
            _logger.Info("S3 put_object ok", new Dictionary<string, object?>
            {
                ["bucket"] = _bucket,
                ["key"] = key,
                ["etag"] = resp.ETag
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden || ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Acesso negado no S3. Verifique IAM/policies do usuário de backup.", ex);
        }

        var head = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
        {
            BucketName = _bucket,
            Key = key
        }, ct);

        if (head.ContentLength != fi.Length)
        {
            throw new InvalidOperationException($"Falha de verificação: tamanho no S3 ({head.ContentLength}) difere do local ({fi.Length}).");
        }

        if (!string.IsNullOrWhiteSpace(sha256Base64))
        {
            var remoteSha = head.Metadata["x-amz-meta-sha256b64"];
            if (string.IsNullOrWhiteSpace(remoteSha) || !string.Equals(remoteSha, sha256Base64, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Falha de verificação: metadata sha256b64 no S3 não confere.");
            }
        }
    }
}
