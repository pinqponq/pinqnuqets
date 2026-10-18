using Microsoft.Extensions.Options;

namespace Pinqponq.Storage.S3.Tests;

internal static class StorageOptionsFactory
{
    public const string BUCKET_NAME = "test-bucket";
    public const string ACCESS_KEY = "test-access-key";
    public const string SECRET_KEY = "test-secret-key";

    public static ObjectStorageOptions Aws() => new()
    {
        BucketName = BUCKET_NAME,
        AccessKey = ACCESS_KEY,
        SecretKey = SECRET_KEY,
        Region = "eu-west-1"
    };

    public static ObjectStorageOptions Compatible(string serviceUrl) => new()
    {
        BucketName = BUCKET_NAME,
        AccessKey = ACCESS_KEY,
        SecretKey = SECRET_KEY,
        ServiceUrl = serviceUrl
    };

    public static (S3ObjectStorage Storage, FakeStorageClient Fake) Storage(ObjectStorageOptions? storageOptions = null)
    {
        var fake = new FakeStorageClient();
        var storage = new S3ObjectStorage(fake, Options.Create(storageOptions ?? Aws()));
        return (storage, fake);
    }
}
