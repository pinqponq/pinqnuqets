namespace Pinqponq.Auth.ApiKey;

/// <summary>Settings for one named API key checked by <see cref="RequireApiKeyAttribute"/>.</summary>
public sealed class ApiKeyOptions
{
    /// <summary>
    /// The shared secret callers must send. When it is empty every request is refused with 401,
    /// so a missing key never opens the endpoint.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>The request header that carries the key, for example <c>X-Internal-Key</c>. Required.</summary>
    public string HeaderName { get; set; } = string.Empty;
}
