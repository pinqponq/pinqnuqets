using Microsoft.Extensions.Options;

namespace Pinqponq.Storage.S3.Tests;

internal static class StorageOptionsFactory
{
    public const string BUCKET_NAME = "test-bucket";

    public static ObjectStorageOptions Aws() => new()
    {
        BucketName = BUCKET_NAME,
        AccessKey = "test-access-key",
        SecretKey = "test-secret-key",
        Region = "eu-west-1"
    };

    public static ObjectStorageOptions Compatible(string serviceUrl) => new()
    {
        BucketName = BUCKET_NAME,
        AccessKey = "test-access-key",
        SecretKey = "test-secret-key",
        ServiceUrl = serviceUrl
    };

    public static (S3ObjectStorage Storage, FakeStorageClient Fake) Storage(ObjectStorageOptions? storageOptions = null)
    {
        var (client, fake) = FakeStorageClient.Create();
        var storage = new S3ObjectStorage(client, Options.Create(storageOptions ?? Aws()));
        return (storage, fake);
    }
}
