using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace Pinqponq.Storage.S3;

/// <summary><see cref="IObjectStorage"/> over the AWS SDK S3 client.</summary>
public sealed class S3ObjectStorage : IObjectStorage
{
    private readonly IAmazonS3 _storageClient;
    private readonly ObjectStorageOptions _storageOptions;

    /// <summary>Creates the storage over an S3 client and its options.</summary>
    public S3ObjectStorage(IAmazonS3 storageClient, IOptions<ObjectStorageOptions> storageOptions)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _storageClient = storageClient ?? throw new ArgumentNullException(nameof(storageClient));
        _storageOptions = storageOptions.Value;
    }

    /// <inheritdoc />
    public async Task Upload(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);

        var putObjectRequest = new PutObjectRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            DisablePayloadSigning = _storageOptions.DisablePayloadSigning
        };

        await _storageClient.PutObjectAsync(putObjectRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> CreateDownloadUrl(string key, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ThrowIfNotPositive(expiry);

        var presignedUrlRequest = CreatePresignedUrlRequest(key, HttpVerb.GET, expiry);
        return _storageClient.GetPreSignedURLAsync(presignedUrlRequest);
    }

    /// <inheritdoc />
    public Task<string> CreateUploadUrl(string key, string contentType, TimeSpan expiry, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ThrowIfNotPositive(expiry);

        var presignedUrlRequest = CreatePresignedUrlRequest(key, HttpVerb.PUT, expiry);
        presignedUrlRequest.ContentType = contentType;
        return _storageClient.GetPreSignedURLAsync(presignedUrlRequest);
    }

    /// <inheritdoc />
    public async Task<StoredObjectMetadata?> GetMetadata(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var metadataRequest = new GetObjectMetadataRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = key
        };

        try
        {
            var metadataResponse = await _storageClient
                .GetObjectMetadataAsync(metadataRequest, cancellationToken)
                .ConfigureAwait(false);
            return new StoredObjectMetadata(metadataResponse.ContentLength, metadataResponse.Headers.ContentType);
        }
        catch (AmazonS3Exception storageException) when (storageException.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DownloadToFile(string key, string destinationFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        using var objectResponse = await GetObject(key, cancellationToken).ConfigureAwait(false);
        await using var destinationStream = File.Create(destinationFilePath);
        await objectResponse.ResponseStream.CopyToAsync(destinationStream, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<byte[]> Download(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var objectResponse = await GetObject(key, cancellationToken).ConfigureAwait(false);
        using var contentBuffer = new MemoryStream();
        await objectResponse.ResponseStream.CopyToAsync(contentBuffer, cancellationToken).ConfigureAwait(false);
        return contentBuffer.ToArray();
    }

    /// <inheritdoc />
    public async Task Copy(string sourceKey, string destinationKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationKey);

        var copyObjectRequest = new CopyObjectRequest
        {
            SourceBucket = _storageOptions.BucketName,
            SourceKey = sourceKey,
            DestinationBucket = _storageOptions.BucketName,
            DestinationKey = destinationKey
        };

        await _storageClient.CopyObjectAsync(copyObjectRequest, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task Delete(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var deleteObjectRequest = new DeleteObjectRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = key
        };

        await _storageClient.DeleteObjectAsync(deleteObjectRequest, cancellationToken).ConfigureAwait(false);
    }

    private async Task<GetObjectResponse> GetObject(string key, CancellationToken cancellationToken)
    {
        var getObjectRequest = new GetObjectRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = key
        };

        try
        {
            return await _storageClient.GetObjectAsync(getObjectRequest, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception storageException) when (storageException.StatusCode == HttpStatusCode.NotFound)
        {
            throw new ObjectNotFoundException(key, storageException);
        }
    }

    private GetPreSignedUrlRequest CreatePresignedUrlRequest(string key, HttpVerb verb, TimeSpan expiry)
    {
        var expiresAt = DateTime.UtcNow.Add(expiry);
        var urlProtocol = ResolveUrlProtocol();

        return new GetPreSignedUrlRequest
        {
            BucketName = _storageOptions.BucketName,
            Key = key,
            Verb = verb,
            Expires = expiresAt,
            Protocol = urlProtocol
        };
    }

    private Protocol ResolveUrlProtocol()
    {
        var usesPlainHttpEndpoint = Uri.TryCreate(_storageOptions.ServiceUrl, UriKind.Absolute, out var serviceUri)
            && serviceUri.Scheme == Uri.UriSchemeHttp;
        return usesPlainHttpEndpoint ? Protocol.HTTP : Protocol.HTTPS;
    }

    private static void ThrowIfNotPositive(TimeSpan expiry)
    {
        if (expiry <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(expiry), expiry, "Expiry must be positive.");
        }
    }
}
