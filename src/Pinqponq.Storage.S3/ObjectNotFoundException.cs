namespace Pinqponq.Storage.S3;

/// <summary>Thrown when a download asks for a key that does not exist in the bucket.</summary>
public sealed class ObjectNotFoundException : Exception
{
    /// <summary>Creates the exception for a missing key.</summary>
    public ObjectNotFoundException(string key, Exception innerException)
        : base($"Object '{key}' was not found.", innerException)
    {
        Key = key;
    }

    /// <summary>The key that was not found.</summary>
    public string Key { get; }
}
