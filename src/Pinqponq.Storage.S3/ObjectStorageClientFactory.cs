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
        return new AmazonS3Client(credentials, CreateClientConfiguration(storageOptions));
    }

    public static AmazonS3Config CreateClientConfiguration(ObjectStorageOptions storageOptions)
    {
        var usesServiceUrl = !string.IsNullOrWhiteSpace(storageOptions.ServiceUrl);
        var forcePathStyle = storageOptions.ForcePathStyle ?? usesServiceUrl;
        var checksumMode = ToSdkChecksumMode(storageOptions.RequestChecksumMode);

        var clientConfiguration = new AmazonS3Config
        {
            ForcePathStyle = forcePathStyle,
            RequestChecksumCalculation = checksumMode.Calculation,
            ResponseChecksumValidation = checksumMode.Validation
        };

        if (usesServiceUrl)
        {
            clientConfiguration.ServiceURL = storageOptions.ServiceUrl;
        }
        else
        {
            clientConfiguration.RegionEndpoint = RegionEndpoint.GetBySystemName(storageOptions.Region);
        }

        if (!string.IsNullOrWhiteSpace(storageOptions.AuthenticationRegion))
        {
            clientConfiguration.AuthenticationRegion = storageOptions.AuthenticationRegion;
        }

        return clientConfiguration;
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
