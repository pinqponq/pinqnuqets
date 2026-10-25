using Microsoft.Extensions.Hosting;

namespace Pinqponq.Hosting.Cors;

internal static class PinqponqCorsOriginRules
{
    internal const string AnyOrigin = "*";

    internal static bool UsesDevelopmentFallback(PinqponqCorsOptions options, IHostEnvironment hostEnvironment)
    {
        return options.AllowAnyOriginInDevelopmentWhenEmpty && hostEnvironment.IsDevelopment();
    }

    internal static bool AllowsEveryOrigin(PinqponqCorsOptions options, IHostEnvironment hostEnvironment)
    {
        return options.AllowedOrigins.Length == 0
            ? UsesDevelopmentFallback(options, hostEnvironment)
            : options.AllowedOrigins.Contains(AnyOrigin);
    }
}
