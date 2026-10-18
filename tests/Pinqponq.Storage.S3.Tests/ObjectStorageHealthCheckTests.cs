using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Storage.S3.Tests;

public sealed class ObjectStorageHealthCheckTests
{
    [Fact]
    public async Task Reachable_bucket_is_healthy()
    {
        var fake = new FakeStorageClient();
        var healthCheck = new ObjectStorageHealthCheck(fake, Options.Create(StorageOptionsFactory.CreateAwsOptions()));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, fake.GetSingleRequest<HeadBucketRequest>().BucketName);
    }

    [Fact]
    public async Task Unreachable_bucket_is_unhealthy_with_the_error()
    {
        var fake = new FakeStorageClient();
        fake.Failure = FakeStorageClient.CreateStatusError(HttpStatusCode.Forbidden);
        var healthCheck = new ObjectStorageHealthCheck(fake, Options.Create(StorageOptionsFactory.CreateAwsOptions()));

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Same(fake.Failure, result.Exception);
        Assert.Equal($"Bucket '{StorageOptionsFactory.BUCKET_NAME}' is unreachable.", result.Description);
    }

    [Fact]
    public void Constructor_rejects_missing_dependencies()
    {
        IAmazonS3 client = new FakeStorageClient();

        Assert.Throws<ArgumentNullException>(
            () => new ObjectStorageHealthCheck(null!, Options.Create(StorageOptionsFactory.CreateAwsOptions())));
        Assert.Throws<ArgumentNullException>(() => new ObjectStorageHealthCheck(client, null!));
    }
}
