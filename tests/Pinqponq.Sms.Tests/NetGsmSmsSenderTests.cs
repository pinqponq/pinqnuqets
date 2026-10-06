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

        Assert.NotNull(handler.LastRequest);
        var uri = handler.LastRequest!.RequestUri!.ToString();
        Assert.Contains("usercode=user1", uri);
        Assert.Contains("password=secret", uri);
        Assert.Contains("gsmno=905551112233", uri);
        Assert.Contains("msgheader=PINQ", uri);
        Assert.Contains("message=Merhaba", uri);
        Assert.Contains("%26", uri);
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

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_missing_UserCode_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler, o => o.UserCode = null);

        var act = () => sender.SendAsync(new SmsMessage { To = "555", Text = "x" });
        await Assert.ThrowsAnyAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task SendAsync_missing_Password_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler, o => o.Password = " ");

        var act = () => sender.SendAsync(new SmsMessage { To = "555", Text = "x" });
        await Assert.ThrowsAnyAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task SendAsync_empty_To_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        var act = () => sender.SendAsync(new SmsMessage { To = "   ", Text = "x" });
        await Assert.ThrowsAnyAsync<ArgumentException>(act);
    }

    [Fact]
    public async Task SendAsync_empty_Text_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        var act = () => sender.SendAsync(new SmsMessage { To = "555", Text = "  " });
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(act);
        Assert.Contains("text", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendAsync_To_without_digits_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        var act = () => sender.SendAsync(new SmsMessage { To = "abc", Text = "x" });
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(act);
        Assert.Contains("digit", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Requests);
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
        var exception = await Assert.ThrowsAnyAsync<NetGsmRejectedException>(act);
        Assert.Contains("NetGSM", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_NetGsm_success_body_with_job_id_succeeds()
    {
        var handler = new CapturingHttpHandler().RespondWith(System.Net.HttpStatusCode.OK, "00 1234567890");
        var sender = Create(handler);

        await sender.SendAsync(new SmsMessage { To = "5551112233", Text = "x" });
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_null_message_throws()
    {
        var handler = new CapturingHttpHandler();
        var sender = Create(handler);

        var act = () => sender.SendAsync(null!);
        await Assert.ThrowsAnyAsync<ArgumentNullException>(act);
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

        Assert.Contains(services, d => d.ServiceType == typeof(ISmsSender));
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
        await Assert.ThrowsAnyAsync<OperationCanceledException>(act);
        Assert.True(handler.RequestCount <= 1);
    }

    [Fact]
    public void SmsOptionsValidator_requires_https_and_rejects_noop_when_disallowed()
    {
        var validator = new SmsOptionsValidator();

        Assert.False(validator.Validate(null, new SmsOptions
        {
            ApiUrl = "http://insecure.example/",
            UserCode = "u",
            Password = "p",
        }).Succeeded);

        Assert.False(validator.Validate(null, new SmsOptions
        {
            ApiUrl = "",
            AllowNoOp = false,
        }).Succeeded);

        Assert.True(validator.Validate(null, new SmsOptions
        {
            ApiUrl = "",
            AllowNoOp = true,
        }).Succeeded);

        Assert.True(validator.Validate(null, new SmsOptions
        {
            ApiUrl = "https://api.example/",
            UserCode = "u",
            Password = "p",
        }).Succeeded);
    }

    [Fact]
    public void AllowNoOp_defaults_to_false()
    {
        Assert.False(new SmsOptions().AllowNoOp);
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

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal(SmsOptions.DefaultRestV2ApiUrl, handler.LastRequest.RequestUri!.ToString());
        Assert.NotNull(handler.LastRequest.Headers.Authorization);
        Assert.Equal("Basic", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal(
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("user1:secret")),
            handler.LastRequest.Headers.Authorization.Parameter);
        Assert.Contains("\"msgheader\":\"PINQ\"", handler.LastRequestBody);
        Assert.Contains("\"msg\":\"Merhaba\"", handler.LastRequestBody);
        Assert.Contains("\"no\":\"905551112233\"", handler.LastRequestBody);
    }

    [Fact]
    public void SmsOptionsValidator_RestV2_allows_empty_ApiUrl_with_default_https()
    {
        var validator = new SmsOptionsValidator();

        Assert.True(validator.Validate(null, new SmsOptions
        {
            Transport = SmsTransport.RestV2,
            ApiUrl = null,
            UserCode = "u",
            Password = "p",
        }).Succeeded);
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
