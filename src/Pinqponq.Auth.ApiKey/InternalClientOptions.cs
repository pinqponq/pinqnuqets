namespace Pinqponq.Auth.ApiKey;

/// <summary>Settings for an <c>HttpClient</c> that calls another service's key-protected endpoints.</summary>
public sealed class InternalClientOptions
{
    /// <summary>The address of the other service, for example <c>https://users.internal</c>. Required, absolute http or https.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The shared secret sent with every request. Required.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The request header that carries the key, for example <c>X-Internal-Key</c>. Required.</summary>
    public string HeaderName { get; set; } = string.Empty;

    /// <summary>How long a request may take, in seconds. Must be positive.</summary>
    public int TimeoutSeconds { get; set; } = 5;
}
