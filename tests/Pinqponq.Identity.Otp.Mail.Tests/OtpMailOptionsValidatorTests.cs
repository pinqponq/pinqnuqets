using Xunit;

namespace Pinqponq.Identity.Otp.Mail.Tests;

public sealed class OtpMailOptionsValidatorTests
{
    private readonly OtpMailOptions _options = new() { ProductName = "Example" };

    private IEnumerable<string> Failures()
    {
        var result = new OtpMailOptionsValidator().Validate(null, _options);
        return result.Failures ?? [];
    }

    [Fact]
    public void Defaults_with_a_product_name_are_valid()
    {
        Assert.True(new OtpMailOptionsValidator().Validate(null, _options).Succeeded);
    }

    [Fact]
    public void Https_and_http_urls_are_valid()
    {
        _options.LogoUrl = "https://example.com/logo.png";
        _options.CallToActionUrl = "http://localhost:5000/";

        Assert.Empty(Failures());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Product_name_is_required(string productName)
    {
        _options.ProductName = productName;

        Assert.Equal("ProductName is required.", Assert.Single(Failures()));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_urls_are_valid_and_hide_their_element(string url)
    {
        _options.LogoUrl = url;
        _options.CallToActionUrl = url;

        Assert.Empty(Failures());
    }

    [Theory]
    [InlineData("/logo.png")]
    [InlineData("ftp://example.com/logo.png")]
    [InlineData("not a url")]
    public void Logo_url_must_be_an_absolute_http_url(string url)
    {
        _options.LogoUrl = url;

        Assert.Equal("LogoUrl must be an absolute http or https URL.", Assert.Single(Failures()));
    }

    [Fact]
    public void Call_to_action_url_must_be_an_absolute_http_url()
    {
        _options.CallToActionUrl = "mailto:someone@example.com";

        Assert.Equal("CallToActionUrl must be an absolute http or https URL.", Assert.Single(Failures()));
    }

    [Theory]
    [InlineData(nameof(OtpMailColors.PageBackground))]
    [InlineData(nameof(OtpMailColors.CardBackground))]
    [InlineData(nameof(OtpMailColors.HeaderBackground))]
    [InlineData(nameof(OtpMailColors.Accent))]
    [InlineData(nameof(OtpMailColors.OnAccent))]
    [InlineData(nameof(OtpMailColors.CodeBackground))]
    [InlineData(nameof(OtpMailColors.Title))]
    [InlineData(nameof(OtpMailColors.Text))]
    [InlineData(nameof(OtpMailColors.Muted))]
    public void Every_color_must_be_a_hex_color(string colorName)
    {
        typeof(OtpMailColors).GetProperty(colorName)!.SetValue(_options.Colors, "red");

        Assert.Equal($"Colors.{colorName} must be a #rgb or #rrggbb color.", Assert.Single(Failures()));
    }

    [Theory]
    [InlineData("#abc")]
    [InlineData("#A1B2C3")]
    public void Short_and_long_hex_colors_are_valid(string color)
    {
        _options.Colors.Accent = color;

        Assert.Empty(Failures());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("#abcd")]
    [InlineData("abc")]
    [InlineData("#ggg")]
    public void Malformed_colors_fail(string? color)
    {
        _options.Colors.Accent = color!;

        Assert.Single(Failures());
    }

    [Fact]
    public void Colors_are_required()
    {
        _options.Colors = null!;

        Assert.Equal("Colors is required.", Assert.Single(Failures()));
    }

    [Theory]
    [InlineData(nameof(OtpMailTexts.Subject))]
    [InlineData(nameof(OtpMailTexts.Title))]
    [InlineData(nameof(OtpMailTexts.Body))]
    [InlineData(nameof(OtpMailTexts.LifetimeLabel))]
    public void Every_language_needs_its_required_texts(string textName)
    {
        var texts = OtpMailTexts.CreateEnglishDefaults();
        typeof(OtpMailTexts).GetProperty(textName)!.SetValue(texts, " ");
        _options.Texts["de"] = texts;

        Assert.Equal(
            "Texts[de] needs a Subject, a Title, a Body and a LifetimeLabel.",
            Assert.Single(Failures()));
    }

    [Fact]
    public void A_language_without_texts_fails()
    {
        _options.Texts["de"] = null!;

        Assert.Equal("Texts[de] is empty.", Assert.Single(Failures()));
    }

    [Fact]
    public void Default_language_without_texts_fails()
    {
        _options.DefaultLanguage = "de";

        Assert.Equal("DefaultLanguage 'de' has no Texts.", Assert.Single(Failures()));
    }

    [Fact]
    public void Default_language_is_matched_case_insensitively()
    {
        _options.DefaultLanguage = "EN";

        Assert.Empty(Failures());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Default_language_is_required(string language)
    {
        _options.DefaultLanguage = language;

        Assert.Equal("DefaultLanguage is required.", Assert.Single(Failures()));
    }

    [Fact]
    public void Every_failure_is_reported()
    {
        _options.ProductName = "";
        _options.LogoUrl = "/logo.png";
        _options.DefaultLanguage = "de";

        Assert.Equal(3, Failures().Count());
    }

    [Fact]
    public void Options_are_required()
    {
        Assert.Throws<ArgumentNullException>(() => new OtpMailOptionsValidator().Validate(null, null!));
    }
}
