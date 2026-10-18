using Xunit;

namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

public sealed class RevenueCatMailOptionsValidatorTests
{
    private static RevenueCatMailOptions ValidOptions()
    {
        var options = new RevenueCatMailOptions();
        options.Brand.Name = "Example";
        return options;
    }

    private static IEnumerable<string> Failures(RevenueCatMailOptions options) =>
        new RevenueCatMailOptionsValidator().Validate(null, options).Failures ?? [];

    [Fact]
    public void Defaults_with_a_brand_name_are_valid()
    {
        var result = new RevenueCatMailOptionsValidator().Validate(null, ValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Every_event_enabled_with_the_default_texts_is_valid()
    {
        var options = ValidOptions();
        foreach (var eventOptions in options.Events.Values)
        {
            eventOptions.Enabled = true;
        }

        Assert.True(new RevenueCatMailOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Brand_name_is_required(string name)
    {
        var options = ValidOptions();
        options.Brand.Name = name;

        Assert.Contains(Failures(options), failure => failure.Contains("Brand.Name"));
    }

    [Theory]
    [InlineData("#abc", true)]
    [InlineData("#A1B2C3", true)]
    [InlineData("abc", false)]
    [InlineData("#abcd", false)]
    [InlineData("#gggggg", false)]
    [InlineData("#123456; background: url(x)", false)]
    [InlineData("red", false)]
    [InlineData(null, false)]
    public void Primary_color_must_be_a_hex_color(string? color, bool isValid)
    {
        var options = ValidOptions();
        options.Brand.PrimaryColor = color!;

        Assert.Equal(isValid, !Failures(options).Any(failure => failure.Contains("PrimaryColor")));
    }

    [Theory]
    [InlineData("https://example.com/logo.png", true)]
    [InlineData("http://example.com/logo.png", true)]
    [InlineData("/logo.png", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("ftp://example.com/logo.png", false)]
    public void Logo_url_must_be_absolute_http(string url, bool isValid)
    {
        var options = ValidOptions();
        options.Brand.LogoUrl = url;

        Assert.Equal(isValid, !Failures(options).Any(failure => failure.Contains("LogoUrl")));
    }

    [Theory]
    [InlineData("https://example.com/account", true)]
    [InlineData("account", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Call_to_action_url_of_an_enabled_event_must_be_absolute_http(string url, bool isValid)
    {
        var options = ValidOptions();
        options.Events[RevenueCatMailEvent.InitialPurchase].CallToActionUrl = url;

        Assert.Equal(isValid, !Failures(options).Any(failure => failure.Contains("CallToActionUrl")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Default_language_is_required(string language)
    {
        var options = ValidOptions();
        options.DefaultLanguage = language;

        var failure = Assert.Single(Failures(options));
        Assert.Contains("DefaultLanguage", failure);
    }

    [Fact]
    public void Paragraphs_must_be_a_list()
    {
        var options = ValidOptions();
        options.Texts["en"].Events[RevenueCatMailEvent.Renewal].Paragraphs = null!;

        var failure = Assert.Single(Failures(options));
        Assert.Contains("Texts[en] Renewal Paragraphs", failure);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Paragraphs_must_not_hold_empty_entries(string? paragraph)
    {
        var options = ValidOptions();
        options.Texts["en"].Events[RevenueCatMailEvent.Renewal].Paragraphs.Add(paragraph!);

        Assert.Contains(Failures(options), failure => failure.Contains("Paragraphs"));
    }

    [Fact]
    public void Empty_paragraph_list_is_valid()
    {
        var options = ValidOptions();
        options.Texts["en"].Events[RevenueCatMailEvent.InitialPurchase].Paragraphs.Clear();

        Assert.True(new RevenueCatMailOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void Enabled_event_needs_a_text_in_the_default_language()
    {
        var options = ValidOptions();
        options.DefaultLanguage = "tr";

        var failure = Assert.Single(Failures(options));
        Assert.Contains("InitialPurchase", failure);
        Assert.Contains("'tr'", failure);
    }

    [Fact]
    public void Default_language_with_texts_but_without_the_enabled_event_fails()
    {
        var options = ValidOptions();
        options.Events[RevenueCatMailEvent.Renewal].Enabled = true;
        options.Texts["en"].Events.Remove(RevenueCatMailEvent.Renewal);

        Assert.Contains(Failures(options), failure => failure.Contains("Renewal"));
    }

    [Fact]
    public void Disabled_event_needs_no_text()
    {
        var options = ValidOptions();
        options.Texts["en"].Events.Remove(RevenueCatMailEvent.Renewal);
        options.Events[RevenueCatMailEvent.Renewal].CallToActionUrl = "not a url";

        Assert.True(new RevenueCatMailOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("", "Title")]
    [InlineData("Subject", "")]
    [InlineData(" ", " ")]
    public void Every_text_needs_a_subject_and_a_title(string subject, string title)
    {
        var options = ValidOptions();
        options.Texts["tr"] = new RevenueCatMailTexts
        {
            Events = { [RevenueCatMailEvent.Expiration] = new RevenueCatMailText { Subject = subject, Title = title } },
        };

        var failure = Assert.Single(Failures(options));
        Assert.Contains("Texts[tr] Expiration", failure);
    }

    [Fact]
    public void All_failures_are_reported_together()
    {
        var options = new RevenueCatMailOptions();
        options.Brand.PrimaryColor = "red";
        options.DefaultLanguage = "tr";

        Assert.Equal(3, Failures(options).Count());
    }

    [Fact]
    public void Null_options_throw()
    {
        Assert.Throws<ArgumentNullException>(() => new RevenueCatMailOptionsValidator().Validate(null, null!));
    }
}
