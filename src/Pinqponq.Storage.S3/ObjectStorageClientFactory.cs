using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace Pinqponq.Storage.S3;

/// <summary>Builds the S3 client from <see cref="ObjectStorageOptions"/>.</summary>
internal static class ObjectStorageClientFactory
{
    public static IAmazonS3 CreateClient(ObjectStorageOptions storageOptions)
    {
        var credentials = new BasicAWSCredentials(storageOptions.AccessKey, storageOptions.SecretKey);
        return new AmazonS3Client(credentials, CreateClientConfig(storageOptions));
    }

    public static AmazonS3Config CreateClientConfig(ObjectStorageOptions storageOptions)
    {
        var usesServiceUrl = !string.IsNullOrWhiteSpace(storageOptions.ServiceUrl);
        var checksumMode = ToSdkChecksumMode(storageOptions.RequestChecksumMode);

        var clientConfig = new AmazonS3Config
        {
            ForcePathStyle = storageOptions.ForcePathStyle ?? usesServiceUrl,
            RequestChecksumCalculation = checksumMode.Calculation,
            ResponseChecksumValidation = checksumMode.Validation
        };

        if (usesServiceUrl)
        {
            clientConfig.ServiceURL = storageOptions.ServiceUrl;
        }
        else
        {
            clientConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(storageOptions.Region);
        }

        if (!string.IsNullOrWhiteSpace(storageOptions.AuthenticationRegion))
        {
            clientConfig.AuthenticationRegion = storageOptions.AuthenticationRegion;
        }

        return clientConfig;
    }

    private static (RequestChecksumCalculation Calculation, ResponseChecksumValidation Validation) ToSdkChecksumMode(
        ObjectStorageChecksumMode checksumMode) =>
        checksumMode switch
        {
            ObjectStorageChecksumMode.WhenRequired =>
                (RequestChecksumCalculation.WHEN_REQUIRED, ResponseChecksumValidation.WHEN_REQUIRED),
            ObjectStorageChecksumMode.WhenSupported =>
                (RequestChecksumCalculation.WHEN_SUPPORTED, ResponseChecksumValidation.WHEN_SUPPORTED),
            _ => throw new ArgumentOutOfRangeException(nameof(checksumMode), checksumMode, "Unknown checksum mode.")
        };
}
