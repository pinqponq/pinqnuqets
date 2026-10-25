namespace Pinqponq.Hosting.Cors;

/// <summary>Settings of the CORS policy.</summary>
public sealed class PinqponqCorsOptions
{
    /// <summary>
    /// The origins that may call the API, for example <c>https://app.example.com</c>. An origin has a scheme,
    /// a host and an optional port, and no path or trailing slash. <c>*</c> alone allows every origin.
    /// There is no default list; an empty list stops the host at startup, except as described for
    /// <see cref="AllowAnyOriginInDevelopmentWhenEmpty"/>.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// When <c>true</c>, responses carry <c>Access-Control-Allow-Credentials</c>. It cannot be combined with
    /// an origin list that allows every origin. Default: <c>false</c>.
    /// </summary>
    public bool AllowCredentials { get; set; }

    /// <summary>The request headers that are allowed. Default: empty, which allows any header.</summary>
    public string[] AllowedHeaders { get; set; } = [];

    /// <summary>The HTTP methods that are allowed. Default: empty, which allows any method.</summary>
    public string[] AllowedMethods { get; set; } = [];

    /// <summary>The response headers the browser may read. Default: empty.</summary>
    public string[] ExposedHeaders { get; set; } = [];

    /// <summary>How long a browser may cache a preflight answer. Default: not set, so the browser decides.</summary>
    public TimeSpan? PreflightMaxAge { get; set; }

    /// <summary>
    /// The name of the policy. When it is not set, the policy is the default policy and <c>app.UseCors()</c>
    /// applies it. When it is set, <c>app.UseCors(name)</c> applies it.
    /// </summary>
    public string? PolicyName { get; set; }

    /// <summary>
    /// When <c>true</c>, an empty <see cref="AllowedOrigins"/> list allows every origin in the Development
    /// environment instead of stopping the host. In every other environment an empty list still stops
    /// the host. Default: <c>false</c>.
    /// </summary>
    public bool AllowAnyOriginInDevelopmentWhenEmpty { get; set; }
}
