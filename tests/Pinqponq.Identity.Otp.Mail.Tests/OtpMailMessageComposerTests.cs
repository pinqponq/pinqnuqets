using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Identity.Otp.Mail.Tests;

public sealed class OtpMailMessageComposerTests
{
    private const string Recipient = "user@example.com";
    private const string Code = "123456";

    private readonly OtpMailOptions _mailOptions = new() { ProductName = "Example" };
    private readonly OtpOptions _otpOptions = new() { Ttl = TimeSpan.FromMinutes(3) };

    private Task<OtpEmailContent> Compose(string? language = null, IOtpMailTemplate? template = null) =>
        new OtpMailMessageComposer(
                new FixedLanguageResolver(language),
                template ?? new OtpMailTemplate(),
                Options.Create(_mailOptions),
                Options.Create(_otpOptions))
            .ComposeEmailAsync(new OtpMessageContext(Recipient, Code, "login"));

    private OtpMailTexts GermanTexts() => new()
    {
        Subject = "{Code} ist Ihr {ProductName}-Code",
        Title = "Ihr Code",
        Body = "Geben Sie den Code in {ProductName} ein.",
        LifetimeLabel = "{Minutes} Minuten gültig",
    };

    [Fact]
    public async Task Default_language_uses_the_english_texts_with_the_product_name()
    {
        var content = await Compose();

        Assert.Equal("Your Example verification code", content.Subject);
        Assert.True(content.IsBodyHtml);
        Assert.Contains("<html lang=\"en\">", content.Body);
        Assert.Contains("<title>Your Example verification code</title>", content.Body);
        Assert.Contains(">Your verification code is 123456.</div>", content.Body);
        Assert.Contains(">Your verification code</h1>", content.Body);
        Assert.Contains(">Hello,</p>", content.Body);
        Assert.Contains(">Enter this code in Example to continue. If you did not ask for it, you can ignore this email.</p>", content.Body);
        Assert.Contains(">Valid for 3 minutes</div>", content.Body);
        Assert.Contains(">123456</div>", content.Body);
        Assert.Contains(">This email was sent automatically by Example.</div>", content.Body);
        Assert.DoesNotContain("{", content.Body);
    }

    [Fact]
    public async Task Resolved_language_with_texts_is_used_and_its_key_is_case_insensitive()
    {
        _mailOptions.Texts["de"] = GermanTexts();

        var content = await Compose("DE");

        Assert.Equal("123456 ist Ihr Example-Code", content.Subject);
        Assert.Contains("<html lang=\"DE\">", content.Body);
        Assert.Contains(">3 Minuten g&#252;ltig</div>", content.Body);
        Assert.Contains(">Geben Sie den Code in Example ein.</p>", content.Body);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task Language_without_texts_falls_back_to_the_default_language(string? language)
    {
        _mailOptions.Texts["de"] = GermanTexts();
        _mailOptions.DefaultLanguage = "de";

        var content = await Compose(language);

        Assert.Equal("123456 ist Ihr Example-Code", content.Subject);
        Assert.Contains("<html lang=\"de\">", content.Body);
    }

    [Fact]
    public async Task Resolver_gets_the_recipient()
    {
        var resolver = new FixedLanguageResolver(null);
        var composer = new OtpMailMessageComposer(
            resolver,
            new OtpMailTemplate(),
            Options.Create(_mailOptions),
            Options.Create(_otpOptions));

        await composer.ComposeEmailAsync(new OtpMessageContext(Recipient, Code, "login"));

        Assert.Equal(Recipient, Assert.Single(resolver.Recipients));
    }

    [Theory]
    [InlineData(180, 3)]
    [InlineData(150, 3)]
    [InlineData(121, 3)]
    [InlineData(120, 2)]
    [InlineData(61, 2)]
    public async Task Lifetime_minutes_follow_the_otp_lifetime_rounded_up(int ttlSeconds, int expectedMinutes)
    {
        _otpOptions.Ttl = TimeSpan.FromSeconds(ttlSeconds);
        var template = new RecordingTemplate();

        await Compose(template: template);

        var model = Assert.Single(template.Models);
        Assert.Equal(expectedMinutes, model.Minutes);
        Assert.Equal($"Valid for {expectedMinutes} minutes", model.LifetimeLabel);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(20)]
    [InlineData(1)]
    public async Task A_lifetime_of_one_minute_or_less_shows_one_minute_with_its_own_label(int ttlSeconds)
    {
        _otpOptions.Ttl = TimeSpan.FromSeconds(ttlSeconds);
        var template = new RecordingTemplate();

        await Compose(template: template);

        var model = Assert.Single(template.Models);
        Assert.Equal(1, model.Minutes);
        Assert.Equal("Valid for 1 minute", model.LifetimeLabel);
    }

    [Fact]
    public async Task One_minute_uses_the_general_label_when_no_one_minute_label_is_set()
    {
        _otpOptions.Ttl = TimeSpan.FromSeconds(60);
        _mailOptions.Texts["en"].LifetimeLabelOneMinute = " ";

        var content = await Compose();

        Assert.Contains(">Valid for 1 minutes</div>", content.Body);
    }

    [Fact]
    public async Task Product_name_and_texts_are_html_encoded()
    {
        _mailOptions.ProductName = "A&B <Shop>";
        _mailOptions.Texts["en"].Body = "Use \"it\" <now>";

        var content = await Compose();

        Assert.Equal("Your A&B <Shop> verification code", content.Subject);
        Assert.Contains("<title>Your A&amp;B &lt;Shop&gt; verification code</title>", content.Body);
        Assert.Contains(">Use &quot;it&quot; &lt;now&gt;</p>", content.Body);
        Assert.Contains("by A&amp;B &lt;Shop&gt;.</div>", content.Body);
        Assert.DoesNotContain("<Shop>", content.Body);
        Assert.DoesNotContain("<now>", content.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Logo_header_is_hidden_without_a_logo_url(string? logoUrl)
    {
        _mailOptions.LogoUrl = logoUrl;

        var content = await Compose();

        Assert.DoesNotContain("<img", content.Body);
    }

    [Fact]
    public async Task Logo_header_shows_the_logo_with_the_product_name_as_alternative_text()
    {
        _mailOptions.LogoUrl = "https://example.com/logo.png?a=1&b=2";
        _mailOptions.Colors.HeaderBackground = "#123456";

        var content = await Compose();

        Assert.Contains(
            "<div style=\"padding: 24px; text-align: center; background-color: #123456;\"><img src=\"https://example.com/logo.png?a=1&amp;b=2\" alt=\"Example\"",
            content.Body);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Button_is_hidden_without_a_call_to_action_url(string? url)
    {
        _mailOptions.CallToActionUrl = url;

        var content = await Compose();

        Assert.DoesNotContain("<a ", content.Body);
        Assert.DoesNotContain("Open Example", content.PlainTextBody);
    }

    [Fact]
    public async Task Model_has_no_button_label_without_a_call_to_action_url()
    {
        var template = new RecordingTemplate();

        await Compose(template: template);

        var model = Assert.Single(template.Models);
        Assert.Null(model.CallToActionUrl);
        Assert.Null(model.CallToActionLabel);
    }

    [Fact]
    public async Task Button_is_hidden_without_a_call_to_action_label()
    {
        _mailOptions.CallToActionUrl = "https://app.example.com/";
        _mailOptions.Texts["en"].CallToActionLabel = null;
        var template = new RecordingTemplate();

        var content = await Compose(template: template);

        var model = Assert.Single(template.Models);
        Assert.Null(model.CallToActionUrl);
        Assert.Null(model.CallToActionLabel);
        Assert.DoesNotContain("https://app.example.com/", content.PlainTextBody);
    }

    [Fact]
    public async Task Button_links_to_the_call_to_action_url_in_the_accent_colors()
    {
        _mailOptions.CallToActionUrl = "https://app.example.com/?x=1&y=2";
        _mailOptions.Colors.Accent = "#abcdef";
        _mailOptions.Colors.OnAccent = "#010203";

        var content = await Compose();

        Assert.Contains(
            "<a href=\"https://app.example.com/?x=1&amp;y=2\" style=\"display: inline-block; background-color: #abcdef; color: #010203;",
            content.Body);
        Assert.Contains(">Open Example</a>", content.Body);
    }

    [Fact]
    public async Task Colors_are_written_into_the_layout()
    {
        var colors = _mailOptions.Colors;
        colors.PageBackground = "#000001";
        colors.CardBackground = "#000002";
        colors.CodeBackground = "#000003";
        colors.Title = "#000004";
        colors.Text = "#000005";
        colors.Muted = "#000006";
        colors.Accent = "#000007";

        var html = (await Compose()).Body;

        Assert.Contains("padding: 24px 12px; background-color: #000001;", html);
        Assert.Contains("margin: 0 auto; background-color: #000002;", html);
        Assert.Contains("text-align: center; background-color: #000003;", html);
        Assert.Contains("text-align: center; color: #000004;\">Your verification code</h1>", html);
        Assert.Contains("color: #000005;\">Hello,</p>", html);
        Assert.Contains("color: #000006;\">Valid for 3 minutes</div>", html);
        Assert.Contains("color: #000007;\">123456</div>", html);
    }

    [Fact]
    public async Task Optional_texts_left_empty_are_left_out()
    {
        var texts = _mailOptions.Texts["en"];
        texts.Greeting = " ";
        texts.Preheader = null;
        texts.Footer = "";
        var template = new RecordingTemplate();

        var content = await Compose(template: template);

        var model = Assert.Single(template.Models);
        Assert.Null(model.Greeting);
        Assert.Null(model.Preheader);
        Assert.Equal("Example", model.Footer);
        Assert.DoesNotContain("Hello,", content.PlainTextBody);
    }

    [Fact]
    public async Task Empty_optional_texts_render_no_paragraph_and_an_empty_preheader()
    {
        var texts = _mailOptions.Texts["en"];
        texts.Greeting = null;
        texts.Preheader = null;
        texts.Footer = null;

        var html = (await Compose()).Body;

        Assert.DoesNotContain("Hello,", html);
        Assert.Contains("opacity: 0;\"></div>", html);
        Assert.Contains("text-align: center; color: #888888;\">Example</div>", html);
    }

    [Fact]
    public async Task Custom_template_renders_the_body_from_the_filled_model()
    {
        _mailOptions.LogoUrl = "https://example.com/logo.png";
        _mailOptions.CallToActionUrl = "https://app.example.com/";
        var template = new RecordingTemplate();

        var content = await Compose(template: template);

        var model = Assert.Single(template.Models);
        Assert.Equal("<custom>123456</custom>", content.Body);
        Assert.Equal("en", model.Language);
        Assert.Equal(Code, model.Code);
        Assert.Equal("Example", model.ProductName);
        Assert.Equal("https://example.com/logo.png", model.LogoUrl);
        Assert.Equal("https://app.example.com/", model.CallToActionUrl);
        Assert.Same(_mailOptions.Colors, model.Colors);
        Assert.Equal("Your Example verification code", model.Subject);
        Assert.Equal("Your verification code is 123456.", model.Preheader);
        Assert.Equal("Your verification code", model.Title);
        Assert.Equal("Hello,", model.Greeting);
        Assert.Equal("Enter this code in Example to continue. If you did not ask for it, you can ignore this email.", model.Body);
        Assert.Equal("Open Example", model.CallToActionLabel);
        Assert.Equal("This email was sent automatically by Example.", model.Footer);
    }

    [Fact]
    public async Task Plain_text_version_holds_the_code_texts_button_and_footer()
    {
        _mailOptions.CallToActionUrl = "https://app.example.com/";

        var content = await Compose();

        Assert.Equal(
            "Your verification code\n\n"
            + "123456\nValid for 3 minutes\n\n"
            + "Hello,\n\n"
            + "Enter this code in Example to continue. If you did not ask for it, you can ignore this email.\n\n"
            + "Open Example: https://app.example.com/\n\n"
            + "-- \nThis email was sent automatically by Example.\n",
            content.PlainTextBody);
    }

    [Fact]
    public async Task Every_placeholder_is_replaced_in_every_text()
    {
        const string all = "{ProductName}|{Code}|{Minutes}";
        _mailOptions.CallToActionUrl = "https://app.example.com/";
        _mailOptions.Texts["en"] = new OtpMailTexts
        {
            Subject = all,
            Preheader = all,
            Title = all,
            Greeting = all,
            Body = all,
            LifetimeLabel = all,
            CallToActionLabel = all,
            Footer = all,
        };
        var template = new RecordingTemplate();

        await Compose(template: template);

        var model = Assert.Single(template.Models);
        const string filled = "Example|123456|3";
        Assert.All(
            new[] { model.Subject, model.Preheader, model.Title, model.Greeting, model.Body, model.LifetimeLabel, model.CallToActionLabel, model.Footer },
            text => Assert.Equal(filled, text));
    }

    [Fact]
    public async Task Compose_requires_a_context()
    {
        var composer = new OtpMailMessageComposer(
            new FixedLanguageResolver(null),
            new OtpMailTemplate(),
            Options.Create(_mailOptions),
            Options.Create(_otpOptions));

        await Assert.ThrowsAsync<ArgumentNullException>(() => composer.ComposeEmailAsync(null!));
    }

    [Fact]
    public void Constructor_requires_every_dependency()
    {
        var resolver = new FixedLanguageResolver(null);
        var template = new OtpMailTemplate();
        var mailOptions = Options.Create(_mailOptions);
        var otpOptions = Options.Create(_otpOptions);

        Assert.Equal("languageResolver", Assert.Throws<ArgumentNullException>(() => new OtpMailMessageComposer(null!, template, mailOptions, otpOptions)).ParamName);
        Assert.Equal("mailTemplate", Assert.Throws<ArgumentNullException>(() => new OtpMailMessageComposer(resolver, null!, mailOptions, otpOptions)).ParamName);
        Assert.Equal("mailOptions", Assert.Throws<ArgumentNullException>(() => new OtpMailMessageComposer(resolver, template, null!, otpOptions)).ParamName);
        Assert.Equal("otpOptions", Assert.Throws<ArgumentNullException>(() => new OtpMailMessageComposer(resolver, template, mailOptions, null!)).ParamName);
    }

    [Fact]
    public async Task Hidden_parts_leave_nothing_behind_in_the_layout()
    {
        _mailOptions.Texts["en"].Greeting = null;

        var html = (await Compose()).Body;

        Assert.Equal(string.Empty, Between(html, "overflow: hidden;\">", "<div style=\"padding: 32px").Trim());
        Assert.Equal(string.Empty, Between(html, ">123456</div>\n", "<p ").Replace("</div>", string.Empty).Trim());
        Assert.Equal(string.Empty, Between(html, "you can ignore this email.</p>", "</div>").Trim());
    }

    [Theory]
    [InlineData(null, "Open")]
    [InlineData("https://app.example.com/", null)]
    public void Template_shows_no_button_when_the_link_or_the_label_is_missing(string? url, string? label)
    {
        var model = new OtpMailModel(
            "en", Code, 3, "Example", null, url, new OtpMailColors(),
            "Subject", null, "Title", null, "Body", "Valid", label, "Footer");

        var html = new OtpMailTemplate().Render(model);

        Assert.DoesNotContain("<a ", html);
    }

    [Fact]
    public void New_texts_and_options_start_empty()
    {
        var texts = new OtpMailTexts();

        Assert.Equal(string.Empty, new OtpMailOptions().ProductName);
        Assert.Equal(string.Empty, texts.Subject);
        Assert.Equal(string.Empty, texts.Title);
        Assert.Equal(string.Empty, texts.Body);
        Assert.Equal(string.Empty, texts.LifetimeLabel);
    }

    private static string Between(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"'{start}' not found");
        startIndex += start.Length;
        var endIndex = text.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex >= 0, $"'{end}' not found");
        return text[startIndex..endIndex];
    }

    [Fact]
    public void Template_requires_a_model()
    {
        Assert.Throws<ArgumentNullException>(() => new OtpMailTemplate().Render(null!));
    }

    [Fact]
    public async Task Default_resolver_returns_no_language()
    {
        Assert.Null(await new DefaultOtpMailLanguageResolver().ResolveLanguageAsync(Recipient));
    }
}
