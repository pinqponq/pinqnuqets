using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Pinqponq.Storage.S3;

/// <summary>Health check that sends a HEAD request for the configured bucket.</summary>
public sealed class ObjectStorageHealthCheck : IHealthCheck
{
    private readonly IAmazonS3 _storageClient;
    private readonly ObjectStorageOptions _storageOptions;

    /// <summary>Creates the health check over the shared S3 client.</summary>
    public ObjectStorageHealthCheck(IAmazonS3 storageClient, IOptions<ObjectStorageOptions> storageOptions)
    {
        ArgumentNullException.ThrowIfNull(storageOptions);
        _storageClient = storageClient ?? throw new ArgumentNullException(nameof(storageClient));
        _storageOptions = storageOptions.Value;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var headBucketRequest = new HeadBucketRequest { BucketName = _storageOptions.BucketName };

        try
        {
            await _storageClient.HeadBucketAsync(headBucketRequest, cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy($"Bucket '{_storageOptions.BucketName}' is unreachable.", exception);
        }
    }
}
