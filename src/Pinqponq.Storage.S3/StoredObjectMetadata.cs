namespace Pinqponq.Storage.S3;

/// <summary>Size and content type of a stored object.</summary>
/// <param name="ContentLength">Object size in bytes.</param>
/// <param name="ContentType">Content type the object was stored with; null when the store returned none.</param>
public sealed record StoredObjectMetadata(long ContentLength, string? ContentType);
