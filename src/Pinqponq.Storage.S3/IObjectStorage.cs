namespace Pinqponq.Storage.S3;

/// <summary>
/// Object storage over one bucket of AWS S3 or an S3-compatible store.
/// Every method throws <see cref="ArgumentException"/> for an empty key.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Uploads <paramref name="content"/> under <paramref name="key"/>, replacing an existing object.</summary>
    Task Upload(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Creates a presigned GET URL that is valid for <paramref name="expiry"/>.</summary>
    Task<string> CreateDownloadUrl(string key, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a presigned PUT URL that is valid for <paramref name="expiry"/>. The content type is part of the
    /// signature: the uploader must send exactly <paramref name="contentType"/> as its Content-Type header.
    /// </summary>
    Task<string> CreateUploadUrl(string key, string contentType, TimeSpan expiry, CancellationToken cancellationToken = default);

    /// <summary>Returns the object's size and content type, or null when the key does not exist.</summary>
    Task<StoredObjectMetadata?> GetMetadata(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes the object to <paramref name="destinationFilePath"/>. Throws <see cref="ObjectNotFoundException"/> when the key does not exist.</summary>
    Task DownloadToFile(string key, string destinationFilePath, CancellationToken cancellationToken = default);

    /// <summary>Reads the whole object into memory. Throws <see cref="ObjectNotFoundException"/> when the key does not exist.</summary>
    Task<byte[]> Download(string key, CancellationToken cancellationToken = default);

    /// <summary>Copies an object to another key in the same bucket.</summary>
    Task Copy(string sourceKey, string destinationKey, CancellationToken cancellationToken = default);

    /// <summary>Deletes the object. Deleting a key that does not exist succeeds.</summary>
    Task Delete(string key, CancellationToken cancellationToken = default);
}
