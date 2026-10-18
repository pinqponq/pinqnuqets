using Xunit;

namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

public sealed class RevenueCatMailTextsTests
{
    [Fact]
    public void English_defaults_cover_every_event_and_name_only_the_brand()
    {
        var texts = RevenueCatMailTexts.CreateEnglishDefaults();

        foreach (var mailEvent in Enum.GetValues<RevenueCatMailEvent>())
        {
            var text = texts.Events[mailEvent];
            Assert.Contains("{BrandName}", text.Subject);
            Assert.False(string.IsNullOrWhiteSpace(text.Title));
            Assert.False(string.IsNullOrWhiteSpace(text.Preheader));
            Assert.NotEmpty(text.Paragraphs);
            Assert.All(text.Paragraphs, paragraph => Assert.False(string.IsNullOrWhiteSpace(paragraph)));
            Assert.Null(text.CallToActionLabel);
        }
    }

    [Fact]
    public void New_text_starts_empty()
    {
        var text = new RevenueCatMailText();

        Assert.Equal(string.Empty, text.Subject);
        Assert.Equal(string.Empty, text.Title);
        Assert.Null(text.Preheader);
        Assert.Null(text.CallToActionLabel);
        Assert.Empty(text.Paragraphs);
    }

    [Fact]
    public void Options_start_with_english_texts_and_only_initial_purchase_enabled()
    {
        var options = new RevenueCatMailOptions();

        Assert.Equal("en", options.DefaultLanguage);
        Assert.True(options.RequirePremiumEntitlement);
        Assert.Equal(string.Empty, options.Brand.Name);
        Assert.Null(options.Brand.LogoUrl);
        Assert.Null(options.Brand.FooterText);
        Assert.Equal("#333333", options.Brand.PrimaryColor);
        Assert.Equal("#ffffff", options.Brand.OnPrimaryColor);
        Assert.True(options.Texts.ContainsKey("EN"));
        Assert.Equal(Enum.GetValues<RevenueCatMailEvent>().Length, options.Events.Count);
        Assert.All(options.Events, entry =>
        {
            Assert.Equal(entry.Key == RevenueCatMailEvent.InitialPurchase, entry.Value.Enabled);
            Assert.Null(entry.Value.CallToActionUrl);
            Assert.Null(entry.Value.TemplateOverride);
        });
    }
}
