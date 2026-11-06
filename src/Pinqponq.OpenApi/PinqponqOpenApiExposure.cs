namespace Pinqponq.OpenApi;

/// <summary>Where the OpenAPI document and the Scalar reference are served.</summary>
public enum PinqponqOpenApiExposure
{
    /// <summary>Only when <c>IHostEnvironment.IsDevelopment()</c> is true. Nothing is mapped in any other environment, so the routes answer 404.</summary>
    DevelopmentOnly = 0,

    /// <summary>In every environment. Use <see cref="PinqponqOpenApiOptions.AuthorizationPolicy"/> to keep anonymous callers out.</summary>
    Always = 1,

    /// <summary>In no environment.</summary>
    Never = 2,
}
