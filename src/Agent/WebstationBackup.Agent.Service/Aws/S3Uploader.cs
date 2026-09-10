using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using WebstationBackup.Agent.Service.Logging;

namespace WebstationBackup.Agent.Service.Aws;

internal sealed class S3Uploader : IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly ILogger _logger;
    private readonly string _bucket;
    private readonly string _keyPrefix;
    private readonly int _maxAttempts;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;

    public S3Uploader(string region, string bucket, string keyPrefix, AWSCredentials credentials, ILogger logger, int maxAttempts, TimeSpan initialDelay, TimeSpan maxDelay)
    {
        _bucket = bucket;
        _keyPrefix = string.IsNullOrWhiteSpace(keyPrefix) ? string.Empty : keyPrefix.Trim().Trim('/') + "/";
        _logger = logger;
        _maxAttempts = Math.Max(1, maxAttempts);
        _initialDelay = initialDelay < TimeSpan.Zero ? TimeSpan.Zero : initialDelay;
        _maxDelay = maxDelay < _initialDelay ? _initialDelay : maxDelay;

        _s3 = new AmazonS3Client(credentials, RegionEndpoint.GetBySystemName(region));
    }

    public async Task<bool> FileAlreadyExistsWithSameSha256Async(string relativePath, string? sha256Base64, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sha256Base64))
        {
            return false;
        }

        var key = _keyPrefix + relativePath.TrimStart('/').Replace('\\', '/');

        try
        {
            var head = await _s3.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = _bucket,
                Key = key,
                ChecksumMode = ChecksumMode.ENABLED
            }, ct);

            var remoteSha = head.ChecksumSHA256;
            return !string.IsNullOrWhiteSpace(remoteSha) && string.Equals(remoteSha, sha256Base64, StringComparison.Ordinal);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden || ex.StatusCode == HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException("Acesso negado no S3. Verifique IAM/policies do usuário de backup.", ex);
        }
    }

    public async Task UploadFileAndVerifyAsync(string absolutePath, string relativePath, string? sha256Base64, CancellationToken ct)
    {
        var key = _keyPrefix + relativePath.TrimStart('/').Replace('\\', '/');

        var fi = new FileInfo(absolutePath);
        if (!fi.Exists)
        {
            throw new FileNotFoundException("Arquivo não encontrado para upload.", absolutePath);
        }

        try
        {
            using var stream = await OpenReadableStreamWithRetryAsync(absolutePath, ct);
            using var sha = SHA256.Create();
            var streamHash = Convert.ToBase64String(sha.ComputeHash(stream));
            if (!string.IsNullOrWhiteSpace(sha256Base64) && !string.Equals(streamHash, sha256Base64, StringComparison.Ordinal))
                throw new IOException("O arquivo mudou apos a criacao do manifesto; upload cancelado.");
            sha256Base64 = streamHash;
            stream.Position = 0;
            var put = new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = stream,
                AutoCloseStream = false,
                ChecksumSHA256 = streamHash
            };

            if (!string.IsNullOrWhiteSpace(sha256Base64))
            {
                put.Metadata.Add("sha256b64", sha256Base64);
            }

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
            Key = key,
            ChecksumMode = ChecksumMode.ENABLED
        }, ct);

        if (head.ContentLength != fi.Length)
        {
            throw new InvalidOperationException($"Falha de verificação: tamanho no S3 ({head.ContentLength}) difere do local ({fi.Length}).");
        }

        if (!string.IsNullOrWhiteSpace(sha256Base64))
        {
            var remoteSha = head.ChecksumSHA256;
            if (string.IsNullOrWhiteSpace(remoteSha) || !string.Equals(remoteSha, sha256Base64, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Falha de verificação: metadata sha256b64 no S3 não confere.");
            }
        }
    }

    private async Task<FileStream> OpenReadableStreamWithRetryAsync(string path, CancellationToken ct)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            catch (Exception ex) when (attempt < _maxAttempts && IsRetryableReadException(ex))
            {
                lastException = ex;
                var delay = CalculateDelay(attempt);
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct);
                }
            }
            catch (Exception ex)
            {
                throw BuildOpenFailure(path, ex, attempt);
            }
        }

        throw BuildOpenFailure(path, lastException ?? new IOException("Falha ao abrir arquivo para upload."), _maxAttempts);
    }

    private TimeSpan CalculateDelay(int attempt)
    {
        var multiplier = Math.Max(1, attempt);
        var delay = TimeSpan.FromMilliseconds(_initialDelay.TotalMilliseconds * multiplier);
        return delay <= _maxDelay ? delay : _maxDelay;
    }

    private static bool IsRetryableReadException(Exception ex)
    {
        return ex switch
        {
            IOException ioEx => IsSharingOrLockViolation(ioEx),
            _ => false
        };
    }

    private static Exception BuildOpenFailure(string path, Exception ex, int attempts)
    {
        var message = ex switch
        {
            PathTooLongException => $"Path muito longo para upload: '{path}'.",
            UnauthorizedAccessException => $"Acesso negado ao abrir o arquivo para upload: '{path}'.",
            IOException ioEx when IsSharingOrLockViolation(ioEx) => $"Arquivo em uso ou bloqueado para leitura apos {attempts} tentativa(s): '{path}'.",
            FileNotFoundException => $"Arquivo nao encontrado para upload: '{path}'.",
            DirectoryNotFoundException => $"Diretorio nao encontrado para upload: '{path}'.",
            _ => $"Falha ao abrir o arquivo para upload: '{path}'. {ex.Message}"
        };

        return new IOException(message, ex);
    }

    private static bool IsSharingOrLockViolation(IOException ex)
    {
        var win32Code = ex.HResult & 0xFFFF;
        return win32Code == 32 || win32Code == 33;
    }

    public void Dispose() => _s3.Dispose();
}
