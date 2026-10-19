using Microsoft.Extensions.Options;

namespace Pinqponq.Hosting.Periodic;

internal sealed class PeriodicJobOptionsValidator : IValidateOptions<PeriodicJobOptions>
{
    // PeriodicTimer and Task.Delay reject a period longer than uint.MaxValue - 1 milliseconds.
    internal static readonly TimeSpan MaximumTimerPeriod = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    public ValidateOptionsResult Validate(string? name, PeriodicJobOptions options)
    {
        var failures = new List<string>();

        if (options.Name is not null && string.IsNullOrWhiteSpace(options.Name))
        {
            failures.Add($"{nameof(PeriodicJobOptions.Name)} of periodic job '{name}' must not be blank.");
        }

        if (options.Interval <= TimeSpan.Zero || options.Interval > MaximumTimerPeriod)
        {
            failures.Add($"{nameof(PeriodicJobOptions.Interval)} of periodic job '{name}' must be greater than zero and at most {MaximumTimerPeriod}.");
        }

        if (options.StartupDelay < TimeSpan.Zero || options.StartupDelay > MaximumTimerPeriod)
        {
            failures.Add($"{nameof(PeriodicJobOptions.StartupDelay)} of periodic job '{name}' must not be negative and at most {MaximumTimerPeriod}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
