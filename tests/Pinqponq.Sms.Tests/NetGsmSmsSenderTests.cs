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
        HttpMessageHandler handler,
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
        var sender = Create(handler, o =>
        {
            o.ApiUrl = "";
            o.AllowNoOp = true;
        });

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
    public async Task SendAsync_empty_Text_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        var act = () => sender.SendAsync(new SmsMessage { To = "555", Text = "  " });
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*text*");
    }

    [Fact]
    public async Task SendAsync_To_without_digits_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        var act = () => sender.SendAsync(new SmsMessage { To = "abc", Text = "x" });
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*digit*");
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task SendAsync_NetGsm_business_error_body_throws()
    {
        var handler = new CapturingHttpHandler().RespondWith(System.Net.HttpStatusCode.OK, "30");
        var sender = Create(handler, o =>
        {
            o.RetryCount = 5;
            o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
        });

        var act = () => sender.SendAsync(new SmsMessage { To = "5551112233", Text = "x" });
        await act.Should().ThrowAsync<NetGsmRejectedException>().WithMessage("*NetGSM*");
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task SendAsync_NetGsm_success_body_with_job_id_succeeds()
    {
        var handler = new CapturingHttpHandler().RespondWith(System.Net.HttpStatusCode.OK, "00 1234567890");
        var sender = Create(handler);

        await sender.SendAsync(new SmsMessage { To = "5551112233", Text = "x" });
        handler.Requests.Should().ContainSingle();
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

    [Fact]
    public async Task SendAsync_caller_cancellation_is_not_retried()
    {
        var handler = new CancellingHttpHandler();
        var sender = Create(handler, o =>
        {
            o.RetryCount = 5;
            o.RetryBaseDelay = TimeSpan.FromMilliseconds(1);
        });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sender.SendAsync(new SmsMessage { To = "5551112233", Text = "x" }, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        handler.RequestCount.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void SmsOptionsValidator_requires_https_and_rejects_noop_when_disallowed()
    {
        var validator = new SmsOptionsValidator();

        validator.Validate(null, new SmsOptions
        {
            ApiUrl = "http://insecure.example/",
            UserCode = "u",
            Password = "p",
        }).Succeeded.Should().BeFalse();

        validator.Validate(null, new SmsOptions
        {
            ApiUrl = "",
            AllowNoOp = false,
        }).Succeeded.Should().BeFalse();

        validator.Validate(null, new SmsOptions
        {
            ApiUrl = "",
            AllowNoOp = true,
        }).Succeeded.Should().BeTrue();

        validator.Validate(null, new SmsOptions
        {
            ApiUrl = "https://api.example/",
            UserCode = "u",
            Password = "p",
        }).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void AllowNoOp_defaults_to_false()
    {
        new SmsOptions().AllowNoOp.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_RestV2_posts_basic_auth_and_json_body()
    {
        var handler = new CapturingHttpHandler()
            .RespondWith(System.Net.HttpStatusCode.OK, """{"code":"00"}""", "application/json");
        var sender = Create(handler, o =>
        {
            o.Transport = SmsTransport.RestV2;
            o.ApiUrl = null;
            o.MsgHeader = "PINQ";
        });

        await sender.SendAsync(new SmsMessage { To = "+90 555 111 2233", Text = "Merhaba" });

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().Should().Be(SmsOptions.DefaultRestV2ApiUrl);
        handler.LastRequest.Headers.Authorization.Should().NotBeNull();
        handler.LastRequest.Headers.Authorization!.Scheme.Should().Be("Basic");
        handler.LastRequest.Headers.Authorization.Parameter.Should().Be(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user1:secret")));
        handler.LastRequestBody.Should().Contain("\"msgheader\":\"PINQ\"");
        handler.LastRequestBody.Should().Contain("\"msg\":\"Merhaba\"");
        handler.LastRequestBody.Should().Contain("\"no\":\"905551112233\"");
    }

    [Fact]
    public void SmsOptionsValidator_RestV2_allows_empty_ApiUrl_with_default_https()
    {
        var validator = new SmsOptionsValidator();

        validator.Validate(null, new SmsOptions
        {
            Transport = SmsTransport.RestV2,
            ApiUrl = null,
            UserCode = "u",
            Password = "p",
        }).Succeeded.Should().BeTrue();
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://unused/") };
    }

    private sealed class CancellingHttpHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            cancellationToken.ThrowIfCancellationRequested();
            throw new TaskCanceledException("cancelled", null, cancellationToken);
        }
    }
}
