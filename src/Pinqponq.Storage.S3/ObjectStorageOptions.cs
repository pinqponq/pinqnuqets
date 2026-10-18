namespace Pinqponq.Storage.S3;

/// <summary>
/// Configuration for <see cref="IObjectStorage"/>. Set either <see cref="ServiceUrl"/>
/// (any S3-compatible store) or <see cref="Region"/> (AWS S3).
/// </summary>
public sealed class ObjectStorageOptions
{
    /// <summary>Bucket every object key refers to.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Access key id of the storage credentials.</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>Secret access key of the storage credentials.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Endpoint of an S3-compatible store, e.g. <c>https://minio.example.com</c>.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>AWS region system name, e.g. <c>us-east-1</c>. Used when <see cref="ServiceUrl"/> is not set.</summary>
    public string? Region { get; set; }

    /// <summary>
    /// Addresses objects as <c>{endpoint}/{bucket}/{key}</c> instead of <c>{bucket}.{endpoint}/{key}</c>.
    /// Null means <c>true</c> when <see cref="ServiceUrl"/> is set and <c>false</c> for AWS.
    /// </summary>
    public bool? ForcePathStyle { get; set; }

    /// <summary>Sends uploads with an unsigned payload. Default <c>false</c>.</summary>
    public bool DisablePayloadSigning { get; set; }

    /// <summary>When the client calculates and validates checksums. Default <see cref="ObjectStorageChecksumMode.WhenRequired"/>.</summary>
    public ObjectStorageChecksumMode RequestChecksumMode { get; set; } = ObjectStorageChecksumMode.WhenRequired;

    /// <summary>Region name used to sign requests to <see cref="ServiceUrl"/>. Null lets the SDK derive it.</summary>
    public string? AuthenticationRegion { get; set; }

    /// <summary>
    /// Points the options at a Cloudflare R2 account and sets the settings R2 needs.
    /// </summary>
    /// <param name="accountId">The Cloudflare account id from the R2 dashboard.</param>
    public ObjectStorageOptions UseCloudflareR2(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        ServiceUrl = $"https://{accountId}.r2.cloudflarestorage.com";
        Region = null;
        ForcePathStyle = true;
        // R2 does not implement chunked payload signing (STREAMING-AWS4-HMAC-SHA256-PAYLOAD).
        DisablePayloadSigning = true;
        // R2 rejects the CRC checksums the AWS SDK adds to every request by default.
        RequestChecksumMode = ObjectStorageChecksumMode.WhenRequired;
        // R2 is region-agnostic and expects requests signed for the "auto" region.
        AuthenticationRegion = "auto";
        return this;
    }
}
