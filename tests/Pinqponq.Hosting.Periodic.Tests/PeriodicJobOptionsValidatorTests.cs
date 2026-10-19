using Xunit;

namespace Pinqponq.Hosting.Periodic.Tests;

public sealed class PeriodicJobOptionsValidatorTests
{
    private readonly PeriodicJobOptionsValidator _validator = new();

    [Fact]
    public void Valid_options_pass()
    {
        var options = new PeriodicJobOptions { Interval = TimeSpan.FromMinutes(1), StartupDelay = TimeSpan.Zero };

        Assert.True(_validator.Validate("job", options).Succeeded);
    }

    [Fact]
    public void Longest_timer_period_passes()
    {
        var longestPeriod = PeriodicJobOptionsValidator.MaximumTimerPeriod;
        var options = new PeriodicJobOptions { Interval = longestPeriod, StartupDelay = longestPeriod };

        Assert.True(_validator.Validate("job", options).Succeeded);
    }

    [Fact]
    public void Default_options_fail_because_interval_is_required()
    {
        var result = _validator.Validate("job", new PeriodicJobOptions());

        Assert.True(result.Failed);
        Assert.Contains("Interval of periodic job 'job'", result.FailureMessage);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Non_positive_interval_fails(int intervalSeconds)
    {
        var options = new PeriodicJobOptions { Interval = TimeSpan.FromSeconds(intervalSeconds) };

        var result = _validator.Validate("job", options);

        Assert.True(result.Failed);
        Assert.Contains("Interval", result.FailureMessage);
    }

    [Fact]
    public void Interval_longer_than_timer_allows_fails()
    {
        var options = new PeriodicJobOptions { Interval = PeriodicJobOptionsValidator.MaximumTimerPeriod + TimeSpan.FromMilliseconds(1) };

        Assert.Contains("Interval", _validator.Validate("job", options).FailureMessage);
    }

    [Fact]
    public void Negative_startup_delay_fails()
    {
        var options = new PeriodicJobOptions { Interval = TimeSpan.FromMinutes(1), StartupDelay = TimeSpan.FromSeconds(-1) };

        var result = _validator.Validate("job", options);

        Assert.True(result.Failed);
        Assert.Contains("StartupDelay", result.FailureMessage);
    }

    [Fact]
    public void Startup_delay_longer_than_timer_allows_fails()
    {
        var options = new PeriodicJobOptions
        {
            Interval = TimeSpan.FromMinutes(1),
            StartupDelay = PeriodicJobOptionsValidator.MaximumTimerPeriod + TimeSpan.FromMilliseconds(1),
        };

        Assert.Contains("StartupDelay", _validator.Validate("job", options).FailureMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_name_fails(string name)
    {
        var options = new PeriodicJobOptions { Interval = TimeSpan.FromMinutes(1), Name = name };

        var result = _validator.Validate("job", options);

        Assert.True(result.Failed);
        Assert.Contains("Name", result.FailureMessage);
    }
}
