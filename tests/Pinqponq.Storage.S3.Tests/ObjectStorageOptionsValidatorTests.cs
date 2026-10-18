using Xunit;

namespace Pinqponq.Storage.S3.Tests;

public sealed class ObjectStorageOptionsValidatorTests
{
    private readonly ObjectStorageOptionsValidator _validator = new();

    [Fact]
    public void Aws_options_are_valid()
    {
        Assert.True(_validator.Validate(null, StorageOptionsFactory.Aws()).Succeeded);
    }

    [Theory]
    [InlineData("https://minio.example.com")]
    [InlineData("http://localhost:9000")]
    public void Service_url_options_are_valid(string serviceUrl)
    {
        Assert.True(_validator.Validate(null, StorageOptionsFactory.Compatible(serviceUrl)).Succeeded);
    }

    [Fact]
    public void Cloudflare_r2_options_are_valid()
    {
        var storageOptions = StorageOptionsFactory.Aws().UseCloudflareR2("account123");

        Assert.True(_validator.Validate(null, storageOptions).Succeeded);
    }

    [Theory]
    [InlineData(nameof(ObjectStorageOptions.BucketName))]
    [InlineData(nameof(ObjectStorageOptions.AccessKey))]
    [InlineData(nameof(ObjectStorageOptions.SecretKey))]
    public void Missing_required_value_fails(string propertyName)
    {
        var storageOptions = StorageOptionsFactory.Aws();
        typeof(ObjectStorageOptions).GetProperty(propertyName)!.SetValue(storageOptions, " ");

        var result = _validator.Validate(null, storageOptions);

        Assert.True(result.Failed);
        Assert.Equal($"{propertyName} is required.", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Missing_endpoint_fails()
    {
        var storageOptions = StorageOptionsFactory.Aws();
        storageOptions.Region = null;

        var result = _validator.Validate(null, storageOptions);

        Assert.True(result.Failed);
        Assert.Equal(
            "Set exactly one of ServiceUrl (S3-compatible store) or Region (AWS S3).",
            Assert.Single(result.Failures!));
    }

    [Fact]
    public void Service_url_and_region_together_fail()
    {
        var storageOptions = StorageOptionsFactory.Compatible("https://minio.example.com");
        storageOptions.Region = "eu-west-1";

        var result = _validator.Validate(null, storageOptions);

        Assert.True(result.Failed);
        Assert.Contains("Set exactly one of ServiceUrl", Assert.Single(result.Failures!));
    }

    [Theory]
    [InlineData("minio.example.com")]
    [InlineData("ftp://minio.example.com")]
    public void Service_url_that_is_not_an_http_url_fails(string serviceUrl)
    {
        var result = _validator.Validate(null, StorageOptionsFactory.Compatible(serviceUrl));

        Assert.True(result.Failed);
        Assert.Equal("ServiceUrl must be an absolute http or https URL.", Assert.Single(result.Failures!));
    }

    [Fact]
    public void Every_failure_is_reported_at_once()
    {
        var result = _validator.Validate(null, new ObjectStorageOptions());

        Assert.Equal(4, result.Failures!.Count());
    }
}
