using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Xunit;

namespace Pinqponq.Storage.S3.Tests;

public sealed class ObjectStorageClientFactoryTests
{
    private static readonly TimeSpan PRESIGNED_URL_EXPIRY = TimeSpan.FromMinutes(15);

    [Fact]
    public void Aws_options_use_the_region_with_virtual_host_addressing()
    {
        var clientConfiguration = ObjectStorageClientFactory.CreateClientConfiguration(StorageOptionsFactory.CreateAwsOptions());

        Assert.Equal(RegionEndpoint.EUWest1, clientConfiguration.RegionEndpoint);
        Assert.Null(clientConfiguration.ServiceURL);
        Assert.False(clientConfiguration.ForcePathStyle);
        Assert.Null(clientConfiguration.AuthenticationRegion);
        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, clientConfiguration.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, clientConfiguration.ResponseChecksumValidation);
    }

    [Fact]
    public void Service_url_options_use_the_endpoint_with_path_addressing()
    {
        var clientConfiguration = ObjectStorageClientFactory.CreateClientConfiguration(
            StorageOptionsFactory.CreateCompatibleOptions("https://minio.example.com"));

        Assert.StartsWith("https://minio.example.com", clientConfiguration.ServiceURL);
        Assert.True(clientConfiguration.ForcePathStyle);
        Assert.Null(clientConfiguration.AuthenticationRegion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Explicit_path_style_wins_over_the_default(bool forcePathStyle)
    {
        var awsOptions = StorageOptionsFactory.CreateAwsOptions();
        awsOptions.ForcePathStyle = forcePathStyle;
        var compatibleOptions = StorageOptionsFactory.CreateCompatibleOptions("https://minio.example.com");
        compatibleOptions.ForcePathStyle = forcePathStyle;

        Assert.Equal(forcePathStyle, ObjectStorageClientFactory.CreateClientConfiguration(awsOptions).ForcePathStyle);
        Assert.Equal(forcePathStyle, ObjectStorageClientFactory.CreateClientConfiguration(compatibleOptions).ForcePathStyle);
    }

    [Fact]
    public void Authentication_region_is_passed_to_the_client()
    {
        var storageOptions = StorageOptionsFactory.CreateCompatibleOptions("https://minio.example.com");
        storageOptions.AuthenticationRegion = "us-east-1";

        Assert.Equal("us-east-1", ObjectStorageClientFactory.CreateClientConfiguration(storageOptions).AuthenticationRegion);
    }

    [Fact]
    public void When_supported_checksum_mode_maps_to_the_sdk_default()
    {
        var storageOptions = StorageOptionsFactory.CreateAwsOptions();
        storageOptions.RequestChecksumMode = ObjectStorageChecksumMode.WhenSupported;

        var clientConfiguration = ObjectStorageClientFactory.CreateClientConfiguration(storageOptions);

        Assert.Equal(RequestChecksumCalculation.WHEN_SUPPORTED, clientConfiguration.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_SUPPORTED, clientConfiguration.ResponseChecksumValidation);
    }

    [Fact]
    public void Unknown_checksum_mode_is_rejected()
    {
        var storageOptions = StorageOptionsFactory.CreateAwsOptions();
        storageOptions.RequestChecksumMode = (ObjectStorageChecksumMode)99;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ObjectStorageClientFactory.CreateClientConfiguration(storageOptions));

        Assert.StartsWith("Unknown checksum mode.", exception.Message);
    }

    [Fact]
    public void Cloudflare_r2_sets_the_endpoint_and_compatibility_settings()
    {
        var storageOptions = StorageOptionsFactory.CreateAwsOptions();
        storageOptions.RequestChecksumMode = ObjectStorageChecksumMode.WhenSupported;

        var returnedOptions = storageOptions.UseCloudflareR2("account123");
        var clientConfiguration = ObjectStorageClientFactory.CreateClientConfiguration(storageOptions);

        Assert.Same(storageOptions, returnedOptions);
        Assert.Equal("https://account123.r2.cloudflarestorage.com", storageOptions.ServiceUrl);
        Assert.Null(storageOptions.Region);
        Assert.True(storageOptions.DisablePayloadSigning);
        Assert.StartsWith("https://account123.r2.cloudflarestorage.com", clientConfiguration.ServiceURL);
        Assert.True(clientConfiguration.ForcePathStyle);
        Assert.Equal("auto", clientConfiguration.AuthenticationRegion);
        Assert.Equal(RequestChecksumCalculation.WHEN_REQUIRED, clientConfiguration.RequestChecksumCalculation);
        Assert.Equal(ResponseChecksumValidation.WHEN_REQUIRED, clientConfiguration.ResponseChecksumValidation);
    }

    [Theory]
    [MemberData(nameof(S3ObjectStorageTests.BlankValues), MemberType = typeof(S3ObjectStorageTests))]
    public void Cloudflare_r2_rejects_an_empty_account_id(string? accountId)
    {
        Assert.ThrowsAny<ArgumentException>(() => StorageOptionsFactory.CreateAwsOptions().UseCloudflareR2(accountId!));
    }

    [Fact]
    public async Task Created_client_presigns_urls_against_the_service_url()
    {
        var storageOptions = StorageOptionsFactory.CreateCompatibleOptions("http://localhost:9000");
        using var client = ObjectStorageClientFactory.CreateClient(storageOptions);
        var expiresAt = DateTime.UtcNow.Add(PRESIGNED_URL_EXPIRY);
        var presignRequest = new GetPreSignedUrlRequest
        {
            BucketName = StorageOptionsFactory.BUCKET_NAME,
            Key = "avatars/1.png",
            Verb = HttpVerb.GET,
            Expires = expiresAt,
            Protocol = Protocol.HTTP
        };

        var presignedUrl = await client.GetPreSignedURLAsync(presignRequest);

        Assert.StartsWith($"http://localhost:9000/{StorageOptionsFactory.BUCKET_NAME}/avatars/1.png?", presignedUrl);
        Assert.Contains($"X-Amz-Credential={StorageOptionsFactory.ACCESS_KEY}", presignedUrl);
    }
}
