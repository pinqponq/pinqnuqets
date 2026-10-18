using Xunit;

namespace Pinqponq.Billing.RevenueCat.Mail.Tests;

public sealed class RevenueCatMailRendererTests
{
    private readonly RevenueCatMailBrand _brand = new() { Name = "Example" };
    private readonly RevenueCatMailEventOptions _eventOptions = new() { Enabled = true };
    private readonly RevenueCatMailText _text = new()
    {
        Subject = "Welcome to {BrandName}",
        Preheader = "Preview for {BrandName}",
        Title = "Hello from {BrandName}",
        Paragraphs = { "First paragraph.", "Second about {BrandName}." },
    };

    private RevenueCatRenderedMail Render(string language = "en") =>
        new RevenueCatMailRenderer().Render(new RevenueCatMailRenderRequest(
            RevenueCatMailEvent.InitialPurchase,
            language,
            _text,
            _brand,
            _eventOptions,
            new RevenueCatEventContext(new RevenueCatEvent { Id = "evt-1", Type = RevenueCatEventType.InitialPurchase }, "21", true)));

    [Fact]
    public void Brand_name_replaces_the_placeholder_in_every_text()
    {
        var mail = Render();

        Assert.Equal("Welcome to Example", mail.Subject);
        Assert.Contains("Preview for Example", mail.HtmlBody);
        Assert.Contains("Hello from Example", mail.HtmlBody);
        Assert.Contains("<title>Welcome to Example</title>", mail.HtmlBody);
        Assert.Contains("Second about Example.", mail.HtmlBody);
        Assert.DoesNotContain("{BrandName}", mail.HtmlBody);
        Assert.DoesNotContain("{", mail.HtmlBody);
    }

    [Fact]
    public void Paragraphs_are_rendered_in_order()
    {
        var html = Render().HtmlBody;

        var first = html.IndexOf(">First paragraph.</p>", StringComparison.Ordinal);
        var second = html.IndexOf(">Second about Example.</p>", StringComparison.Ordinal);
        Assert.True(first > 0 && second > first);
    }

    [Fact]
    public void Language_and_primary_color_are_written_into_the_layout()
    {
        _brand.PrimaryColor = "#abcdef";

        var html = Render("tr").HtmlBody;

        Assert.Contains("<html lang=\"tr\">", html);
        Assert.Contains("background-color: #abcdef;", html);
    }

    [Fact]
    public void On_primary_color_is_used_for_the_brand_name_and_the_button_label()
    {
        _brand.OnPrimaryColor = "#101010";
        _text.CallToActionLabel = "Open";
        _eventOptions.CallToActionUrl = "https://example.com/account";

        var html = Render().HtmlBody;

        Assert.Contains("font-weight: bold; color: #101010;\">Example</span>", html);
        Assert.Contains("color: #101010; font-size: 15px; font-weight: bold; text-decoration: none;", html);
        Assert.DoesNotContain("#ffffff;\">Example", html);
    }

    [Fact]
    public void Html_in_brand_and_texts_is_encoded()
    {
        _brand.Name = "<b>Brand</b>";
        _brand.FooterText = "Footer & <i>co</i>";
        _text.Title = "<script>alert(1)</script>";
        _text.Paragraphs = ["Tom & \"Jerry\" <img src=x>"];
        _text.CallToActionLabel = "<Open>";
        _eventOptions.CallToActionUrl = "https://example.com/?a=1&b=\"2\"";

        var html = Render().HtmlBody;

        Assert.DoesNotContain("<b>Brand</b>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x>", html);
        Assert.DoesNotContain("<i>co</i>", html);
        Assert.DoesNotContain("<Open>", html);
        Assert.Contains("&lt;b&gt;Brand&lt;/b&gt;", html);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html);
        Assert.Contains("Tom &amp; &quot;Jerry&quot; &lt;img src=x&gt;", html);
        Assert.Contains("Footer &amp; &lt;i&gt;co&lt;/i&gt;", html);
        Assert.Contains("href=\"https://example.com/?a=1&amp;b=&quot;2&quot;\"", html);
        Assert.Contains("&lt;Open&gt;", html);
    }

    [Fact]
    public void Placeholder_text_inside_a_value_is_not_filled_again()
    {
        _text.Title = "Literal {FooterText}";

        var html = Render().HtmlBody;

        Assert.Contains("Literal {FooterText}", html);
    }

    [Fact]
    public void Header_shows_the_brand_name_without_a_logo()
    {
        var html = Render().HtmlBody;

        Assert.DoesNotContain("<img", html);
        Assert.Contains(">Example</span>", html);
    }

    [Fact]
    public void Header_shows_the_logo_when_configured()
    {
        _brand.LogoUrl = "https://example.com/logo.png";

        var html = Render().HtmlBody;

        Assert.Contains("<img src=\"https://example.com/logo.png\" alt=\"Example\"", html);
        Assert.DoesNotContain(">Example</span>", html);
    }

    [Fact]
    public void Footer_defaults_to_the_brand_name()
    {
        var mail = Render();

        Assert.Contains(">Example</div>", mail.HtmlBody);
        Assert.EndsWith("-- \nExample\n", mail.PlainTextBody);
    }

    [Fact]
    public void Footer_text_replaces_the_brand_placeholder()
    {
        _brand.FooterText = "© {BrandName}";

        var mail = Render();

        Assert.Contains(">&#169; Example</div>", mail.HtmlBody);
        Assert.EndsWith("-- \n© Example\n", mail.PlainTextBody);
    }

    [Fact]
    public void Button_is_rendered_only_with_both_label_and_link()
    {
        _text.CallToActionLabel = "Open {BrandName}";
        _eventOptions.CallToActionUrl = null;
        Assert.DoesNotContain("<a ", Render().HtmlBody);

        _text.CallToActionLabel = null;
        _eventOptions.CallToActionUrl = "https://example.com/account";
        Assert.DoesNotContain("<a ", Render().HtmlBody);

        _text.CallToActionLabel = "Open {BrandName}";
        var mail = Render();
        Assert.Contains("<a href=\"https://example.com/account\"", mail.HtmlBody);
        Assert.Contains(">Open Example</a>", mail.HtmlBody);
        Assert.Contains("Open Example: https://example.com/account\n", mail.PlainTextBody);
    }

    [Fact]
    public void Plain_text_has_title_paragraphs_and_footer_without_markup()
    {
        var plainText = Render().PlainTextBody;

        Assert.Equal("Hello from Example\n\nFirst paragraph.\n\nSecond about Example.\n\n-- \nExample\n", plainText);
    }

    [Fact]
    public void Missing_preheader_renders_empty()
    {
        _text.Preheader = null;

        var html = Render().HtmlBody;

        Assert.Contains("opacity: 0;\"></div>", html);
    }

    [Fact]
    public void Template_override_replaces_the_layout_and_gets_every_value()
    {
        _brand.LogoUrl = "https://example.com/logo.png";
        _brand.PrimaryColor = "#123456";
        _text.CallToActionLabel = "Go";
        _eventOptions.CallToActionUrl = "https://example.com/go";
        _eventOptions.TemplateOverride =
            "{Language}|{BrandName}|{PrimaryColor}|{OnPrimaryColor}|{LogoUrl}|{Subject}|{Preheader}|{Title}|{CallToActionLabel}|{CallToActionUrl}|{FooterText}|{Unknown}";

        var mail = Render("de");

        Assert.Equal(
            "de|Example|#123456|#ffffff|https://example.com/logo.png|Welcome to Example|Preview for Example|Hello from Example|Go|https://example.com/go|Example|{Unknown}",
            mail.HtmlBody);
        Assert.Equal("Welcome to Example", mail.Subject);
        Assert.Contains("Go: https://example.com/go", mail.PlainTextBody);
    }

    [Fact]
    public void Template_override_gets_empty_values_without_logo_and_button()
    {
        _eventOptions.TemplateOverride = "[{LogoUrl}][{CallToActionHtml}][{CallToActionUrl}][{CallToActionLabel}]";

        Assert.Equal("[][][][]", Render().HtmlBody);
    }

    [Fact]
    public void Template_override_gets_the_markup_placeholders()
    {
        _text.CallToActionLabel = "Go";
        _eventOptions.CallToActionUrl = "https://example.com/go";
        _eventOptions.TemplateOverride = "<div>{HeaderHtml}</div>{ParagraphsHtml}{CallToActionHtml}";

        var html = Render().HtmlBody;

        Assert.StartsWith("<div><span", html);
        Assert.Contains(">First paragraph.</p><p", html);
        Assert.EndsWith(">Go</a></p>", html);
    }

    [Fact]
    public void Null_request_throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RevenueCatMailRenderer().Render(null!));
    }
}
