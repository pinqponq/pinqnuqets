using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Mail.DependencyInjection;
using Xunit;

namespace Pinqponq.Mail.Tests;

public sealed class SmtpEmailSenderValidationTests
{
    private static SmtpEmailSender Create() =>
        new(Options.Create(new SmtpOptions
        {
            SmtpHost = "localhost",
            SmtpPort = 1025,
            FromEmail = "noreply@pinqponq.test",
            EnableSsl = false,
        }));

    [Theory]
    [InlineData(null, "s", "b", "Recipient")]
    [InlineData("a@b.com", null, "b", "subject")]
    [InlineData("a@b.com", "s", null, "body")]
    public async Task SendAsync_requires_fields(string? to, string? subject, string? body, string because)
    {
        var sender = Create();
        var message = new EmailMessage
        {
            To = to!,
            Subject = subject!,
            Body = body!,
        };

        var act = () => sender.SendAsync(message);
        (await act.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().ContainEquivalentOf(because);
    }

    [Fact]
    public async Task SendAsync_null_message_throws()
    {
        var sender = Create();
        var act = () => sender.SendAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void AddPinqponqMail_registers_sender()
    {
        var services = new ServiceCollection();
        services.AddPinqponqMail(o =>
        {
            o.SmtpHost = "localhost";
            o.FromEmail = "a@b.com";
        });

        using var sp = services.BuildServiceProvider();
        sp.GetService<IEmailSender>().Should().BeOfType<SmtpEmailSender>();
    }

    [Fact]
    public void AddPinqponqMail_from_missing_section_throws()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var act = () => services.AddPinqponqMail(config, "Smtp");
        act.Should().Throw<InvalidOperationException>();
    }
}
