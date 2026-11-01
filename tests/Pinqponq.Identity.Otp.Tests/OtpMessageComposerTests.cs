using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Identity.Otp.Tests;

public sealed class OtpMessageComposerTests
{
    private const string TestPepper = "0123456789abcdef0123456789abcdef";

    private readonly InMemoryOtpStore _store = new();
    private readonly CapturingSmsSender _sms = new();
    private readonly CapturingEmailSender _email = new();

    private OtpService Create(IOtpMessageComposer composer, Action<OtpOptions>? configure = null)
    {
        var options = new OtpOptions { HashPepper = TestPepper };
        configure?.Invoke(options);
        return new OtpService(_store, _sms, _email, new AllowAllOtpSendRateLimiter(), composer, Options.Create(options));
    }

    [Fact]
    public async Task Without_a_composer_the_email_is_built_from_the_templates_as_before()
    {
        var options = new OtpOptions
        {
            HashPepper = TestPepper,
            EmailSubjectTemplate = "Subject {0}",
            EmailBodyTemplate = "Body {0}",
        };
        var service = new OtpService(_store, _sms, _email, new AllowAllOtpSendRateLimiter(), Options.Create(options));

        await service.GenerateAndSendAsync("user@example.com");

        var message = Assert.Single(_email.Sent);
        var code = message.Body["Body ".Length..];
        Assert.Equal("user@example.com", message.To);
        Assert.Equal("Subject " + code, message.Subject);
        Assert.Equal(6, code.Length);
        Assert.True(message.IsBodyHtml);
        Assert.Null(message.PlainTextBody);
    }

    [Fact]
    public async Task Template_composer_fills_both_templates_with_the_code()
    {
        var composer = new TemplateOtpMessageComposer(Options.Create(new OtpOptions
        {
            EmailSubjectTemplate = "Code {0} inside",
            EmailBodyTemplate = "Use {0} now",
        }));

        var content = await composer.ComposeEmailAsync(new OtpMessageContext("user@example.com", "123456", "login"));

        Assert.Equal("Code 123456 inside", content.Subject);
        Assert.Equal("Use 123456 now", content.Body);
        Assert.True(content.IsBodyHtml);
        Assert.Null(content.PlainTextBody);
    }

    [Fact]
    public async Task Registered_composer_builds_the_email_and_gets_the_sent_code()
    {
        var composer = new RecordingComposer();
        var service = Create(composer);

        await service.GenerateAndSendAsync("user@example.com", purpose: "register");

        var context = Assert.Single(composer.Contexts);
        var message = Assert.Single(_email.Sent);
        Assert.Equal("user@example.com", context.Recipient);
        Assert.Equal("register", context.Purpose);
        Assert.Equal("user@example.com", message.To);
        Assert.Equal("Composed subject", message.Subject);
        Assert.Equal("<p>" + context.Code + "</p>", message.Body);
        Assert.False(message.IsBodyHtml);
        Assert.Equal("Plain " + context.Code, message.PlainTextBody);
        Assert.Equal(
            OtpVerifyStatus.Success,
            await service.VerifyAsync("user@example.com", context.Code, purpose: "register"));
    }

    [Fact]
    public async Task Sms_codes_do_not_use_the_composer()
    {
        var composer = new RecordingComposer();
        var service = Create(composer);

        await service.GenerateAndSendAsync("+905551112233");

        Assert.Empty(composer.Contexts);
        Assert.Single(_sms.Sent);
    }

    [Fact]
    public async Task Composer_failure_removes_the_stored_code()
    {
        var composer = new RecordingComposer { ExceptionToThrow = new InvalidOperationException("compose failed") };
        var service = Create(composer);

        var send = () => service.GenerateAndSendAsync("user@example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(send);
        Assert.Empty(_email.Sent);
        Assert.Equal(0, _store.Count);
    }

    [Fact]
    public void Service_requires_a_composer()
    {
        var create = () => new OtpService(
            _store,
            _sms,
            _email,
            new AllowAllOtpSendRateLimiter(),
            null!,
            Options.Create(new OtpOptions { HashPepper = TestPepper }));

        var exception = Assert.Throws<ArgumentNullException>(create);
        Assert.Equal("messageComposer", exception.ParamName);
    }

    [Fact]
    public void Service_with_a_composer_requires_options()
    {
        var create = () => new OtpService(_store, _sms, _email, new AllowAllOtpSendRateLimiter(), new RecordingComposer(), null!);

        var exception = Assert.Throws<ArgumentNullException>(create);
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public async Task Email_code_without_an_email_sender_names_the_registration_to_add()
    {
        var composer = new RecordingComposer();
        var service = new OtpService(
            _store,
            _sms,
            null,
            new AllowAllOtpSendRateLimiter(),
            composer,
            Options.Create(new OtpOptions { HashPepper = TestPepper }));

        var send = () => service.GenerateAndSendAsync("user@example.com");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(send);
        Assert.Contains("AddPinqponqMail", exception.Message, StringComparison.Ordinal);
        Assert.Empty(composer.Contexts);
    }

    [Fact]
    public async Task Template_composer_requires_a_context()
    {
        var composer = new TemplateOtpMessageComposer(Options.Create(new OtpOptions()));

        var compose = () => composer.ComposeEmailAsync(null!);

        await Assert.ThrowsAsync<ArgumentNullException>(compose);
    }

    [Fact]
    public void Template_composer_requires_options()
    {
        var create = () => new TemplateOtpMessageComposer(null!);

        Assert.Throws<ArgumentNullException>(create);
    }

    private sealed class RecordingComposer : IOtpMessageComposer
    {
        public List<OtpMessageContext> Contexts { get; } = [];

        public Exception? ExceptionToThrow { get; init; }

        public Task<OtpEmailContent> ComposeEmailAsync(OtpMessageContext context, CancellationToken cancellationToken = default)
        {
            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            Contexts.Add(context);
            return Task.FromResult(new OtpEmailContent
            {
                Subject = "Composed subject",
                Body = "<p>" + context.Code + "</p>",
                IsBodyHtml = false,
                PlainTextBody = "Plain " + context.Code,
            });
        }
    }
}
