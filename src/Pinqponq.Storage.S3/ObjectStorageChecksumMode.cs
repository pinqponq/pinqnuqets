namespace Pinqponq.Storage.S3;

/// <summary>When the S3 client calculates request checksums and validates response checksums.</summary>
public enum ObjectStorageChecksumMode
{
    /// <summary>Only for operations that require a checksum. Accepted by AWS S3 and S3-compatible stores.</summary>
    WhenRequired,

    /// <summary>For every operation that supports one (the AWS SDK default).</summary>
    WhenSupported
}
