namespace Pinqponq.Cache;

/// <summary>
/// A distributed cache over Redis. Object overloads serialize with System.Text.Json.
/// </summary>
public interface ICacheService
{
    /// <summary>Gets a raw string value, or null when the key is absent.</summary>
    Task<string?> GetStringAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Sets a raw string value with an optional expiry (falls back to the configured default).</summary>
    Task SetStringAsync(string key, string value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets and deserializes a value, or default when the key is absent or the payload
    /// is not valid JSON for <typeparamref name="T"/>.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>Serializes and sets a value with an optional expiry.</summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken cancellationToken = default);

    /// <summary>Removes a key. Returns true if it existed.</summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Returns whether a key exists.</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
