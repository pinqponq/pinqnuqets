using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Sms.DependencyInjection;
using Pinqponq.TestSupport;
using Xunit;

namespace Pinqponq.Sms.Tests;

public sealed class NetGsmSmsSenderTests
{
    private static NetGsmSmsSender Create(
        CapturingHttpHandler handler,
        Action<SmsOptions>? configure = null)
    {
        var options = new SmsOptions
        {
            ApiUrl = "https://api.netgsm.example/sms/send/get/",
            UserCode = "user1",
            Password = "secret",
            MsgHeader = "PINQ",
            RetryCount = 1,
            RetryBaseDelay = TimeSpan.FromMilliseconds(1),
        };
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));
        var factory = services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>();
        return new NetGsmSmsSender(factory, Options.Create(options));
    }

    [Fact]
    public async Task SendAsync_builds_expected_query()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        await sender.SendAsync(new SmsMessage { To = "+90 555 111 2233", Text = "Merhaba & test" });

        handler.LastRequest.Should().NotBeNull();
        var uri = handler.LastRequest!.RequestUri!.ToString();
        uri.Should().Contain("usercode=user1");
        uri.Should().Contain("password=secret");
        uri.Should().Contain("gsmno=905551112233");
        uri.Should().Contain("msgheader=PINQ");
        uri.Should().Contain("message=Merhaba");
        uri.Should().Contain("%26");
    }

    [Fact]
    public async Task SendAsync_with_empty_ApiUrl_is_noop()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler, o => o.ApiUrl = "");

        await sender.SendAsync(new SmsMessage { To = "555", Text = "x" });

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_missing_UserCode_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler, o => o.UserCode = null);

        var act = () => sender.SendAsync(new SmsMessage { To = "555", Text = "x" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendAsync_missing_Password_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler, o => o.Password = " ");

        var act = () => sender.SendAsync(new SmsMessage { To = "555", Text = "x" });
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SendAsync_empty_To_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        var act = () => sender.SendAsync(new SmsMessage { To = "   ", Text = "x" });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SendAsync_To_without_digits_is_noop()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        await sender.SendAsync(new SmsMessage { To = "abc", Text = "x" });

        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_null_message_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        var act = () => sender.SendAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void AddPinqponqSms_registers_sender()
    {
        var services = new ServiceCollection();
        services.AddPinqponqSms(o =>
        {
            o.ApiUrl = "https://example/";
            o.UserCode = "u";
            o.Password = "p";
        });

        services.Should().Contain(d => d.ServiceType == typeof(ISmsSender));
    }

    private sealed class StubHttpClientFactory(CapturingHttpHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://unused/") };
    }
}
