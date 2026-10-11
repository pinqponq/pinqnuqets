using Microsoft.Extensions.Options;

namespace Pinqponq.Observability;

internal sealed class ObservabilityOptionsValidator : IValidateOptions<ObservabilityOptions>
{
    public ValidateOptionsResult Validate(string? name, ObservabilityOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.HttpCollectionName))
        {
            failures.Add($"{ObservabilityOptions.SectionName}:{nameof(ObservabilityOptions.HttpCollectionName)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.JobsCollectionName))
        {
            failures.Add($"{ObservabilityOptions.SectionName}:{nameof(ObservabilityOptions.JobsCollectionName)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientCollectionName))
        {
            failures.Add($"{ObservabilityOptions.SectionName}:{nameof(ObservabilityOptions.ClientCollectionName)} is required.");
        }

        if (options.MaxLoggedBodyBytes <= 0)
        {
            failures.Add($"{ObservabilityOptions.SectionName}:{nameof(ObservabilityOptions.MaxLoggedBodyBytes)} must be greater than 0.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
