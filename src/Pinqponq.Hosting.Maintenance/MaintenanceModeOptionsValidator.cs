using Microsoft.Extensions.Options;

namespace Pinqponq.Hosting.Maintenance;

internal sealed class MaintenanceModeOptionsValidator : IValidateOptions<MaintenanceModeOptions>
{
    private const int MinimumStatusCode = 400;
    private const int MaximumStatusCode = 599;

    public ValidateOptionsResult Validate(string? name, MaintenanceModeOptions options)
    {
        var failures = new List<string>();

        if (options.StatusCode is < MinimumStatusCode or > MaximumStatusCode)
        {
            failures.Add($"{nameof(MaintenanceModeOptions.StatusCode)} must be from {MinimumStatusCode} to {MaximumStatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(options.ResponseCode))
        {
            failures.Add($"{nameof(MaintenanceModeOptions.ResponseCode)} must not be blank.");
        }

        if (string.IsNullOrWhiteSpace(options.EventName))
        {
            failures.Add($"{nameof(MaintenanceModeOptions.EventName)} must not be blank.");
        }

        if (options.RetryAfterSeconds is <= 0)
        {
            failures.Add($"{nameof(MaintenanceModeOptions.RetryAfterSeconds)} must be greater than zero when set.");
        }

        foreach (var pathPrefix in options.ExemptPathPrefixes.Where(pathPrefix => !IsPathPrefix(pathPrefix)))
        {
            failures.Add($"{nameof(MaintenanceModeOptions.ExemptPathPrefixes)} entry '{pathPrefix}' must start with '/', such as '/health'.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsPathPrefix(string? pathPrefix) =>
        pathPrefix is not null && pathPrefix.StartsWith('/');
}
