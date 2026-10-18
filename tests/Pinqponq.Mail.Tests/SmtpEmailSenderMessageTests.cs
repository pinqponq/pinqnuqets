using System.Net.Mime;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pinqponq.Mail.Tests;

public sealed class SmtpEmailSenderMessageTests
{
    private static SmtpEmailSender Create() =>
        new(Options.Create(new SmtpOptions
        {
            SmtpHost = "localhost",
            SmtpPort = 1025,
            FromEmail = "noreply@pinqponq.test",
            EnableSsl = false,
        }));

    [Fact]
    public void Html_body_with_plain_text_is_sent_as_plain_text_then_html()
    {
        using var mailMessage = Create().CreateMailMessage(new EmailMessage
        {
            To = "a@b.com",
            Subject = "s",
            Body = "<p>html</p>",
            PlainTextBody = "plain",
        });

        Assert.Equal("plain", mailMessage.Body);
        Assert.False(mailMessage.IsBodyHtml);
        var htmlView = Assert.Single(mailMessage.AlternateViews);
        Assert.Equal(MediaTypeNames.Text.Html, htmlView.ContentType.MediaType);
        using var reader = new StreamReader(htmlView.ContentStream);
        Assert.Equal("<p>html</p>", reader.ReadToEnd());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Html_body_without_plain_text_stays_a_single_html_body(string? plainText)
    {
        using var mailMessage = Create().CreateMailMessage(new EmailMessage
        {
            To = "a@b.com",
            Subject = "s",
            Body = "<p>html</p>",
            PlainTextBody = plainText,
        });

        Assert.Equal("<p>html</p>", mailMessage.Body);
        Assert.True(mailMessage.IsBodyHtml);
        Assert.Empty(mailMessage.AlternateViews);
    }

    [Fact]
    public void Plain_text_body_ignores_the_plain_text_alternative()
    {
        using var mailMessage = Create().CreateMailMessage(new EmailMessage
        {
            To = "a@b.com",
            Subject = "s",
            Body = "body",
            IsBodyHtml = false,
            PlainTextBody = "other",
        });

        Assert.Equal("body", mailMessage.Body);
        Assert.False(mailMessage.IsBodyHtml);
        Assert.Empty(mailMessage.AlternateViews);
    }

    [Fact]
    public void Message_carries_sender_subject_and_recipients()
    {
        using var mailMessage = Create().CreateMailMessage(new EmailMessage
        {
            To = "a@b.com; c@d.com",
            Cc = "e@f.com",
            Bcc = "g@h.com",
            Subject = "subject",
            Body = "body",
        });

        Assert.Equal("noreply@pinqponq.test", mailMessage.From!.Address);
        Assert.Equal("subject", mailMessage.Subject);
        Assert.Equal(["a@b.com", "c@d.com"], mailMessage.To.Select(address => address.Address));
        Assert.Equal("e@f.com", Assert.Single(mailMessage.CC).Address);
        Assert.Equal("g@h.com", Assert.Single(mailMessage.Bcc).Address);
    }
}
