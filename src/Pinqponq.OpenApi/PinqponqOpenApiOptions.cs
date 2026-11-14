namespace Pinqponq.OpenApi;

/// <summary>OpenAPI and Scalar settings of an application. Validated at startup.</summary>
public sealed class PinqponqOpenApiOptions
{
    /// <summary>Default configuration section name.</summary>
    public const string SectionName = "OpenApi";

    /// <summary>Value of <c>info.title</c>. When <c>null</c>, ASP.NET Core's own title is kept.</summary>
    public string? Title { get; set; }

    /// <summary>Value of <c>info.description</c>. When <c>null</c>, the description is not changed.</summary>
    public string? Description { get; set; }

    /// <summary>The Bearer security scheme.</summary>
    public BearerAuthenticationOptions BearerAuthentication { get; } = new();

    /// <summary>The API key security scheme.</summary>
    public ApiKeyAuthenticationOptions ApiKeyAuthentication { get; } = new();

    /// <summary>
    /// Which operations get a security requirement. Default
    /// <see cref="OpenApi.SecurityRequirementScope.AuthorizedOperations"/>.
    /// </summary>
    public SecurityRequirementScope SecurityRequirementScope { get; set; } = SecurityRequirementScope.AuthorizedOperations;

    /// <summary>
    /// Sets <c>servers</c> to the scheme, host and path base of the request that asks for the document, without a
    /// trailing slash. Default <c>true</c>. When <c>false</c>, the entry ASP.NET Core builds from the same request
    /// is kept, and it ends with a slash. Behind a proxy the application must call <c>UseForwardedHeaders</c> before the OpenAPI
    /// endpoint, or the proxy's internal address is published.
    /// </summary>
    public bool ServerUrlFromRequest { get; set; } = true;

    /// <summary>How schema ids are built. Default <see cref="OpenApi.SchemaIdNaming.Default"/>.</summary>
    public SchemaIdNaming SchemaIdNaming { get; set; } = SchemaIdNaming.Default;

    /// <summary>
    /// Where the document and the reference are served. Default
    /// <see cref="PinqponqOpenApiExposure.DevelopmentOnly"/>, which is safe in production.
    /// </summary>
    public PinqponqOpenApiExposure ExposeIn { get; set; } = PinqponqOpenApiExposure.DevelopmentOnly;

    /// <summary>
    /// Authorization policy required to read the document and the reference. When <c>null</c>, no authorization
    /// is required. An empty string requires an authenticated user and no named policy.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Route of the document. It must contain <c>{documentName}</c>. The document is named
    /// <see cref="PinqponqOpenApiDefaults.DocumentName"/>. Default <c>/openapi/{documentName}.json</c>.
    /// </summary>
    public string OpenApiRoutePattern { get; set; } = "/openapi/{documentName}.json";

    /// <summary>Route prefix of the Scalar reference. Default <c>/scalar</c>, which serves <c>/scalar/v1</c>.</summary>
    public string ScalarRoutePrefix { get; set; } = "/scalar";
}
