using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Pinqponq.TestSupport.Fixtures;
using Xunit;

namespace Pinqponq.Mail.Tests;

[Collection(MailHogCollection.Name)]
public sealed class SmtpEmailSenderIntegrationTests
{
    private readonly MailHogCollectionFixture _fixture;

    public SmtpEmailSenderIntegrationTests(MailHogCollectionFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Integration")]
    public async Task SendAsync_delivers_message_to_mailhog()
    {
        var marker = Guid.NewGuid().ToString("N");
        var sender = new SmtpEmailSender(Options.Create(new SmtpOptions
        {
            SmtpHost = _fixture.SmtpHost,
            SmtpPort = _fixture.SmtpMappedPort,
            FromEmail = "noreply@pinqponq.test",
            FromName = "Pinqponq Tests",
            EnableSsl = false,
            SmtpUsername = "",
            SmtpPassword = "",
        }));

        await sender.SendAsync(new EmailMessage
        {
            To = "user@example.com",
            Cc = "cc@example.com",
            Bcc = "bcc@example.com",
            Subject = $"Hello {marker}",
            Body = $"<p>Body {marker}</p>",
            IsBodyHtml = true,
        });

        using var http = new HttpClient { BaseAddress = new Uri(_fixture.ApiBaseUrl) };
        JsonElement? found = null;
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(250);
            var json = await http.GetFromJsonAsync<JsonElement>("/api/v2/messages");
            if (json.TryGetProperty("items", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    var subject = item.GetProperty("Content").GetProperty("Headers").GetProperty("Subject")[0]
                        .GetString();
                    if (subject is not null && subject.Contains(marker, StringComparison.Ordinal))
                    {
                        found = item;
                        break;
                    }
                }
            }

            if (found is not null)
            {
                break;
            }
        }

        found.Should().NotBeNull($"MailHog should receive message with marker {marker}");
        var to = found!.Value.GetProperty("Content").GetProperty("Headers").GetProperty("To")[0].GetString();
        to.Should().Contain("user@example.com");
    }
}
