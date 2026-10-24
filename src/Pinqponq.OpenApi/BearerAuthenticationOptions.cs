namespace Pinqponq.OpenApi;

/// <summary>The HTTP Bearer security scheme published in the document.</summary>
public sealed class BearerAuthenticationOptions
{
    /// <summary>Publishes the scheme. Default <c>true</c>. The scheme is still left out when <see cref="AuthenticationScheme"/> is not registered.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Key of the scheme in <c>components.securitySchemes</c>. Default <c>Bearer</c>.</summary>
    public string SchemeName { get; set; } = "Bearer";

    /// <summary>
    /// Name of the ASP.NET Core authentication scheme that must be registered for the document to publish this
    /// scheme. Default <c>Bearer</c>, the name <c>AddJwtBearer()</c> registers. Set to <c>null</c> to publish the
    /// scheme without checking.
    /// </summary>
    public string? AuthenticationScheme { get; set; } = "Bearer";

    /// <summary>Value of <c>bearerFormat</c>. Default <c>JWT</c>.</summary>
    public string BearerFormat { get; set; } = "JWT";

    /// <summary>Text shown with the scheme. Default <c>Bearer access token.</c></summary>
    public string? Description { get; set; } = "Bearer access token.";
}
