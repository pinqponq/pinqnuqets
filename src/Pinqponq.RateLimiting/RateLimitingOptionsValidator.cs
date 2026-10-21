using Microsoft.Extensions.Options;

namespace Pinqponq.RateLimiting;

internal sealed class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    private const string GlobalLimitName = "global limit";

    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.UserIdClaimType))
        {
            failures.Add($"{nameof(RateLimitingOptions.UserIdClaimType)} is required.");
        }

        foreach (var (policyName, policy) in options.Policies)
        {
            if (string.IsNullOrWhiteSpace(policyName))
            {
                failures.Add("A rate limit policy name must not be empty.");
                continue;
            }

            ValidatePolicy($"policy '{policyName}'", policy, failures);
        }

        for (var globalLimitIndex = 0; globalLimitIndex < options.GlobalLimits.Count; globalLimitIndex++)
        {
            ValidatePolicy($"{GlobalLimitName} {globalLimitIndex}", options.GlobalLimits[globalLimitIndex], failures);
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidatePolicy(string policyDescription, RateLimitPolicyOptions? policy, List<string> failures)
    {
        if (policy is null)
        {
            failures.Add($"Rate limit {policyDescription} has no settings.");
            return;
        }

        if (policy.PermitLimit <= 0)
        {
            failures.Add($"{nameof(RateLimitPolicyOptions.PermitLimit)} of rate limit {policyDescription} must be greater than 0.");
        }

        if (policy.Window <= TimeSpan.Zero)
        {
            failures.Add($"{nameof(RateLimitPolicyOptions.Window)} of rate limit {policyDescription} must be greater than 0.");
        }

        var isCustom = policy.PartitionBy == RateLimitPartitionSource.Custom;
        if (isCustom && policy.PartitionKeyResolver is null)
        {
            failures.Add($"{nameof(RateLimitPolicyOptions.PartitionKeyResolver)} of rate limit {policyDescription} is required when {nameof(RateLimitPolicyOptions.PartitionBy)} is {nameof(RateLimitPartitionSource.Custom)}.");
        }

        if (!isCustom && policy.PartitionKeyResolver is not null)
        {
            failures.Add($"{nameof(RateLimitPolicyOptions.PartitionKeyResolver)} of rate limit {policyDescription} is used only when {nameof(RateLimitPolicyOptions.PartitionBy)} is {nameof(RateLimitPartitionSource.Custom)}.");
        }

        if (!isCustom && policy.IsPartitionKeySecret)
        {
            failures.Add($"{nameof(RateLimitPolicyOptions.IsPartitionKeySecret)} of rate limit {policyDescription} is used only when {nameof(RateLimitPolicyOptions.PartitionBy)} is {nameof(RateLimitPartitionSource.Custom)}.");
        }
    }
}
