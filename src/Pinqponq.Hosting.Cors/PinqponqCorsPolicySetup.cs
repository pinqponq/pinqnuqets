using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Pinqponq.Hosting.Cors;

internal sealed class PinqponqCorsPolicySetup(
    IOptions<PinqponqCorsOptions> options,
    IHostEnvironment hostEnvironment) : IConfigureOptions<CorsOptions>
{
    public void Configure(CorsOptions corsOptions)
    {
        var settings = options.Value;
        var policy = BuildPolicy(settings);

        if (settings.PolicyName is null)
        {
            corsOptions.AddDefaultPolicy(policy);
        }
        else
        {
            corsOptions.AddPolicy(settings.PolicyName, policy);
        }
    }

    private CorsPolicy BuildPolicy(PinqponqCorsOptions settings)
    {
        var builder = new CorsPolicyBuilder();

        if (AllowsEveryOrigin(settings))
        {
            builder.AllowAnyOrigin();
        }
        else
        {
            builder.WithOrigins(settings.AllowedOrigins);
        }

        if (settings.AllowedHeaders.Length == 0)
        {
            builder.AllowAnyHeader();
        }
        else
        {
            builder.WithHeaders(settings.AllowedHeaders);
        }

        if (settings.AllowedMethods.Length == 0)
        {
            builder.AllowAnyMethod();
        }
        else
        {
            builder.WithMethods(settings.AllowedMethods);
        }

        if (settings.ExposedHeaders.Length > 0)
        {
            builder.WithExposedHeaders(settings.ExposedHeaders);
        }

        if (settings.AllowCredentials)
        {
            builder.AllowCredentials();
        }

        if (settings.PreflightMaxAge is { } maxAge)
        {
            builder.SetPreflightMaxAge(maxAge);
        }

        return builder.Build();
    }

    private bool AllowsEveryOrigin(PinqponqCorsOptions settings)
    {
        if (settings.AllowedOrigins.Length == 0)
        {
            return settings.AllowAnyOriginInDevelopmentWhenEmpty && hostEnvironment.IsDevelopment();
        }

        return settings.AllowedOrigins.Contains(PinqponqCorsOptionsValidator.AnyOrigin);
    }
}
