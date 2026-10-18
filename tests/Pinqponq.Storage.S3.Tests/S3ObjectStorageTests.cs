using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Xunit;

namespace Pinqponq.Storage.S3.Tests;

public sealed class S3ObjectStorageTests
{
    private static readonly TimeSpan PRESIGNED_URL_EXPIRY = TimeSpan.FromMinutes(15);

    public static TheoryData<string?> BlankValues => new() { null, "", "   " };

    [Theory]
    [MemberData(nameof(BlankValues))]
    public async Task Every_method_rejects_an_empty_key_without_calling_storage(string? key)
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        var destinationFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.Upload(key!, new MemoryStream(), "image/png"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.CreateDownloadUrl(key!, PRESIGNED_URL_EXPIRY));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.CreateUploadUrl(key!, "image/png", PRESIGNED_URL_EXPIRY));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.GetMetadata(key!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.DownloadToFile(key!, destinationFilePath));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.Download(key!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.Copy(key!, "destination.png"));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.Copy("source.png", key!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.Delete(key!));

        Assert.Empty(fake.Requests);
        Assert.False(File.Exists(destinationFilePath));
    }

    [Fact]
    public async Task Upload_puts_the_content_into_the_bucket()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        using var content = new MemoryStream([1, 2, 3]);

        await storage.Upload("avatars/1.png", content, "image/png");

        var putRequest = fake.GetSingleRequest<PutObjectRequest>();
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, putRequest.BucketName);
        Assert.Equal("avatars/1.png", putRequest.Key);
        Assert.Same(content, putRequest.InputStream);
        Assert.Equal("image/png", putRequest.ContentType);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Upload_takes_payload_signing_from_the_options(bool disablePayloadSigning)
    {
        var storageOptions = StorageOptionsFactory.CreateAwsOptions();
        storageOptions.DisablePayloadSigning = disablePayloadSigning;
        var (storage, fake) = StorageOptionsFactory.CreateStorage(storageOptions);

        await storage.Upload("avatars/1.png", new MemoryStream(), "image/png");

        Assert.Equal(disablePayloadSigning, fake.GetSingleRequest<PutObjectRequest>().DisablePayloadSigning);
    }

    [Fact]
    public async Task Upload_rejects_a_missing_stream()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();

        await Assert.ThrowsAsync<ArgumentNullException>(() => storage.Upload("avatars/1.png", null!, "image/png"));

        Assert.Empty(fake.Requests);
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public async Task Upload_rejects_an_empty_content_type(string? contentType)
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.Upload("avatars/1.png", new MemoryStream(), contentType!));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task CreateDownloadUrl_presigns_a_get_that_expires_in_utc()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        var earliestExpiry = DateTime.UtcNow.Add(PRESIGNED_URL_EXPIRY);

        var downloadUrl = await storage.CreateDownloadUrl("avatars/1.png", PRESIGNED_URL_EXPIRY);

        var latestExpiry = DateTime.UtcNow.Add(PRESIGNED_URL_EXPIRY);
        var presignRequest = fake.GetSingleRequest<GetPreSignedUrlRequest>();
        Assert.Equal(FakeStorageClient.PRESIGNED_URL, downloadUrl);
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, presignRequest.BucketName);
        Assert.Equal("avatars/1.png", presignRequest.Key);
        Assert.Equal(HttpVerb.GET, presignRequest.Verb);
        Assert.Null(presignRequest.ContentType);
        AssertUtcExpiry(presignRequest, earliestExpiry, latestExpiry);
    }

    [Fact]
    public async Task CreateUploadUrl_presigns_a_put_bound_to_the_content_type()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        var earliestExpiry = DateTime.UtcNow.Add(PRESIGNED_URL_EXPIRY);

        var uploadUrl = await storage.CreateUploadUrl("videos/1.mp4", "video/mp4", PRESIGNED_URL_EXPIRY);

        var latestExpiry = DateTime.UtcNow.Add(PRESIGNED_URL_EXPIRY);
        var presignRequest = fake.GetSingleRequest<GetPreSignedUrlRequest>();
        Assert.Equal(FakeStorageClient.PRESIGNED_URL, uploadUrl);
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, presignRequest.BucketName);
        Assert.Equal("videos/1.mp4", presignRequest.Key);
        Assert.Equal(HttpVerb.PUT, presignRequest.Verb);
        Assert.Equal("video/mp4", presignRequest.ContentType);
        AssertUtcExpiry(presignRequest, earliestExpiry, latestExpiry);
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public async Task CreateUploadUrl_rejects_an_empty_content_type(string? contentType)
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.CreateUploadUrl("videos/1.mp4", contentType!, PRESIGNED_URL_EXPIRY));

        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Presigned_urls_reject_an_expiry_that_is_not_positive(int expirySeconds)
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        var expiry = TimeSpan.FromSeconds(expirySeconds);

        var downloadUrlException = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.CreateDownloadUrl("avatars/1.png", expiry));
        var uploadUrlException = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => storage.CreateUploadUrl("avatars/1.png", "image/png", expiry));

        Assert.StartsWith("Expiry must be positive.", downloadUrlException.Message);
        Assert.StartsWith("Expiry must be positive.", uploadUrlException.Message);

        Assert.Empty(fake.Requests);
    }

    [Theory]
    [InlineData(null, Protocol.HTTPS)]
    [InlineData("https://storage.example", Protocol.HTTPS)]
    [InlineData("http://localhost:9000", Protocol.HTTP)]
    [InlineData("HTTP://localhost:9000", Protocol.HTTP)]
    public async Task Presigned_urls_use_the_scheme_of_the_endpoint(string? serviceUrl, Protocol expectedProtocol)
    {
        var storageOptions = serviceUrl is null ? StorageOptionsFactory.CreateAwsOptions() : StorageOptionsFactory.CreateCompatibleOptions(serviceUrl);
        var (storage, fake) = StorageOptionsFactory.CreateStorage(storageOptions);

        await storage.CreateDownloadUrl("avatars/1.png", PRESIGNED_URL_EXPIRY);
        await storage.CreateUploadUrl("avatars/1.png", "image/png", PRESIGNED_URL_EXPIRY);

        Assert.All(fake.Requests, request => Assert.Equal(expectedProtocol, Assert.IsType<GetPreSignedUrlRequest>(request).Protocol));
    }

    [Fact]
    public async Task GetMetadata_returns_size_and_content_type()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        fake.MetadataResponse = new GetObjectMetadataResponse { ContentLength = 2048 };
        fake.MetadataResponse.Headers.ContentType = "image/png";

        var metadata = await storage.GetMetadata("avatars/1.png");

        Assert.Equal(new StoredObjectMetadata(2048, "image/png"), metadata);
        var metadataRequest = fake.GetSingleRequest<GetObjectMetadataRequest>();
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, metadataRequest.BucketName);
        Assert.Equal("avatars/1.png", metadataRequest.Key);
    }

    [Fact]
    public async Task GetMetadata_returns_null_for_a_missing_key()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        fake.Failure = FakeStorageClient.CreateStatusError(HttpStatusCode.NotFound);

        Assert.Null(await storage.GetMetadata("avatars/missing.png"));
    }

    [Fact]
    public async Task GetMetadata_passes_other_storage_errors_on()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        fake.Failure = FakeStorageClient.CreateStatusError(HttpStatusCode.Forbidden);

        var storageException = await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.GetMetadata("avatars/1.png"));

        Assert.Same(fake.Failure, storageException);
    }

    [Fact]
    public async Task Download_returns_the_object_content()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        fake.ObjectContent = [4, 5, 6];

        var content = await storage.Download("avatars/1.png");

        Assert.Equal([4, 5, 6], content);
        var getRequest = fake.GetSingleRequest<GetObjectRequest>();
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, getRequest.BucketName);
        Assert.Equal("avatars/1.png", getRequest.Key);
    }

    [Fact]
    public async Task Download_throws_object_not_found_for_a_missing_key()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        fake.Failure = FakeStorageClient.CreateStatusError(HttpStatusCode.NotFound);

        var notFound = await Assert.ThrowsAsync<ObjectNotFoundException>(() => storage.Download("avatars/missing.png"));

        Assert.Equal("avatars/missing.png", notFound.Key);
        Assert.Same(fake.Failure, notFound.InnerException);
        Assert.Contains("avatars/missing.png", notFound.Message);
    }

    [Fact]
    public async Task Download_passes_other_storage_errors_on()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        fake.Failure = FakeStorageClient.CreateStatusError(HttpStatusCode.Forbidden);

        await Assert.ThrowsAsync<AmazonS3Exception>(() => storage.Download("avatars/1.png"));
    }

    [Fact]
    public async Task DownloadToFile_writes_the_object_to_the_file()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        fake.ObjectContent = [7, 8, 9];
        var destinationFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        try
        {
            await storage.DownloadToFile("avatars/1.png", destinationFilePath);

            Assert.Equal([7, 8, 9], await File.ReadAllBytesAsync(destinationFilePath));
            Assert.Equal("avatars/1.png", fake.GetSingleRequest<GetObjectRequest>().Key);
        }
        finally
        {
            File.Delete(destinationFilePath);
        }
    }

    [Fact]
    public async Task DownloadToFile_throws_object_not_found_and_creates_no_file_for_a_missing_key()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();
        fake.Failure = FakeStorageClient.CreateStatusError(HttpStatusCode.NotFound);
        var destinationFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var notFound = await Assert.ThrowsAsync<ObjectNotFoundException>(
            () => storage.DownloadToFile("avatars/missing.png", destinationFilePath));

        Assert.Equal("avatars/missing.png", notFound.Key);
        Assert.False(File.Exists(destinationFilePath));
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public async Task DownloadToFile_rejects_an_empty_destination(string? destinationFilePath)
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => storage.DownloadToFile("avatars/1.png", destinationFilePath!));

        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task Copy_copies_inside_the_bucket()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();

        await storage.Copy("uploads/1.png", "avatars/1.png");

        var copyRequest = fake.GetSingleRequest<CopyObjectRequest>();
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, copyRequest.SourceBucket);
        Assert.Equal("uploads/1.png", copyRequest.SourceKey);
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, copyRequest.DestinationBucket);
        Assert.Equal("avatars/1.png", copyRequest.DestinationKey);
    }

    [Fact]
    public async Task Delete_deletes_the_key_from_the_bucket()
    {
        var (storage, fake) = StorageOptionsFactory.CreateStorage();

        await storage.Delete("avatars/1.png");

        var deleteRequest = fake.GetSingleRequest<DeleteObjectRequest>();
        Assert.Equal(StorageOptionsFactory.BUCKET_NAME, deleteRequest.BucketName);
        Assert.Equal("avatars/1.png", deleteRequest.Key);
    }

    [Fact]
    public void Constructor_rejects_missing_dependencies()
    {
        IAmazonS3 client = new FakeStorageClient();

        Assert.Throws<ArgumentNullException>(
            () => new S3ObjectStorage(null!, Microsoft.Extensions.Options.Options.Create(StorageOptionsFactory.CreateAwsOptions())));
        Assert.Throws<ArgumentNullException>(() => new S3ObjectStorage(client, null!));
    }

    private static void AssertUtcExpiry(GetPreSignedUrlRequest presignRequest, DateTime earliestExpiry, DateTime latestExpiry)
    {
        var expiresAt = Assert.IsType<DateTime>(presignRequest.Expires);
        Assert.Equal(DateTimeKind.Utc, expiresAt.Kind);
        Assert.InRange(expiresAt, earliestExpiry, latestExpiry);
    }
}
