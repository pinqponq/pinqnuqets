namespace Pinqponq.OpenApi;

/// <summary>An API key security scheme sent in a request header. Off by default.</summary>
public sealed class ApiKeyAuthenticationOptions
{
    /// <summary>Publishes the scheme. Default <c>false</c>. <see cref="HeaderName"/> is required when this is <c>true</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>Name of the request header that carries the key. There is no default.</summary>
    public string? HeaderName { get; set; }

    /// <summary>Key of the scheme in <c>components.securitySchemes</c>. Default <c>ApiKey</c>.</summary>
    public string SchemeName { get; set; } = "ApiKey";

    /// <summary>Text shown with the scheme. Default none.</summary>
    public string? Description { get; set; }
}
