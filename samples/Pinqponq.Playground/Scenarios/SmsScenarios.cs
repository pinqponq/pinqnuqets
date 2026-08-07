using Pinqponq.Playground.Infrastructure;
using Pinqponq.Playground.Scenarios.Support;
using Pinqponq.Sms;
using Pinqponq.Sms.DependencyInjection;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Scenarios for <c>Pinqponq.Sms</c>, run against the console's own fake NetGSM endpoint —
/// so the real named <c>HttpClient</c>, query construction and Polly pipeline all execute
/// without needing a provider account or an internet connection.
/// </summary>
public static class SmsScenarios
{
    private const string Package = "Pinqponq.Sms";

    private static readonly ScenarioField ToField =
        new("to", "Recipient", ScenarioFieldKind.Text, "+90 555 111 22 33");

    private static readonly ScenarioField TextField =
        new("text", "Message", ScenarioFieldKind.Text, "Pinqponq test message");

    public static IEnumerable<Scenario> Create()
    {
        yield return SendHappyPath();
        yield return RestV2Send();
        yield return RetryThenSuccess();
        yield return RetryExhausted();
        yield return BusinessRejectNotRetried();
        yield return Guards();
        yield return HttpsRequired();
    }

    private static Scenario SendHappyPath() => new(
        new ScenarioDescriptor
        {
            Id = "sms.send",
            PackageId = Package,
            Title = "Send an SMS (GET)",
            Summary = "Sends a real HTTP request to the console's own fake NetGSM endpoint and "
                      + "shows the query parameters the package built, as-is. ApiUrl must be "
                      + "HTTPS (validator).",
            Fields =
            [
                ToField,
                TextField,
                new ScenarioField("msgHeader", "MsgHeader", ScenarioFieldKind.Text, "PINQPONQ"),
            ],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            await using var host = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = "playground";
                    sms.Password = "playground-secret";
                    sms.MsgHeader = context.Input.Text("msgHeader");
                });

                SmsSupport.TagOutgoingRequests(services, context.RunId);
            });

            await host.GetRequiredService<ISmsSender>().SendAsync(
                new SmsMessage { To = context.Input.Text("to"), Text = context.Input.Text("text") },
                context.CancellationToken);

            context.Step("SendAsync completed");

            var requests = fake.Requests;
            context.Require("Fake endpoint received exactly one request", requests.Count == 1, $"{requests.Count} requests");

            var request = requests[0];
            context.Check(
                "Phone number reduced to digits only",
                request.GsmNo?.All(char.IsDigit) == true,
                request.GsmNo);
            context.Check("Message text preserved", request.Message == context.Input.Text("text"), request.Message);
            context.Check("MsgHeader sent", request.MsgHeader == context.Input.Text("msgHeader"));
            context.Check("GET transport", request.Method == "GET", request.Method);

            context.Artifact("received request", new
            {
                method = request.Method,
                gsmno = request.GsmNo,
                message = request.Message,
                msgheader = request.MsgHeader,
                usercode = request.UserCode,
                rawQuery = request.RawQuery,
            });
        });

    private static Scenario RestV2Send() => new(
        new ScenarioDescriptor
        {
            Id = "sms.rest-v2",
            PackageId = Package,
            Title = "Send an SMS (RestV2 POST + Basic Auth)",
            Summary = "SmsTransport.RestV2 uses a JSON body and Basic Auth. The fake endpoint "
                      + "receives a POST; the Authorization header and the msgheader/messages fields are visible.",
            Fields = [ToField, TextField, new ScenarioField("msgHeader", "MsgHeader", ScenarioFieldKind.Text, "PINQPONQ")],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            await using var host = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.Transport = SmsTransport.RestV2;
                    sms.ApiUrl = context.FakeSmsRestV2Url;
                    sms.UserCode = "playground";
                    sms.Password = "playground-secret";
                    sms.MsgHeader = context.Input.Text("msgHeader");
                });

                SmsSupport.TagOutgoingRequests(services, context.RunId);
            });

            await host.GetRequiredService<ISmsSender>().SendAsync(
                new SmsMessage { To = context.Input.Text("to"), Text = context.Input.Text("text") },
                context.CancellationToken);

            context.Require("POST request received", fake.Requests.Count == 1 && fake.Requests[0].Method == "POST");
            var request = fake.Requests[0];
            context.Check("Basic Auth used", request.UserCode == "(Basic)");
            context.Check(
                "JSON body parsed",
                request.Message == context.Input.Text("text")
                && !string.IsNullOrEmpty(request.Body),
                request.Body);
            context.Artifact("received request", new
            {
                request.Method,
                request.GsmNo,
                request.Message,
                request.MsgHeader,
                request.UserCode,
                request.Body,
            });
        });

    private static Scenario RetryThenSuccess() => new(
        new ScenarioDescriptor
        {
            Id = "sms.retry",
            PackageId = Package,
            Title = "Retries on a transient error",
            Summary = "The fake endpoint is told to respond to the first N requests with 500. The "
                      + "package's Polly pipeline retries the attempts; the exponential backoff "
                      + "(with jitter) between requests is directly visible from the delays.",
            Fields =
            [
                ToField,
                TextField,
                new ScenarioField("failCount", "How many requests should return 500", ScenarioFieldKind.Number, "2"),
                new ScenarioField("retryCount", "RetryCount", ScenarioFieldKind.Number, "3"),
                new ScenarioField("retryBaseDelayMs", "RetryBaseDelay (ms)", ScenarioFieldKind.Duration, "100"),
            ],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            var failCount = context.Input.Int("failCount");
            fake.FailNext(failCount);
            context.Step($"Fake endpoint configured to return 500 for the first {failCount} requests");

            await using var host = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = "playground";
                    sms.Password = "playground-secret";
                    sms.RetryCount = context.Input.Int("retryCount");
                    sms.RetryBaseDelay = context.Input.Duration("retryBaseDelayMs");
                });

                SmsSupport.TagOutgoingRequests(services, context.RunId);
            });

            await host.GetRequiredService<ISmsSender>().SendAsync(
                new SmsMessage { To = context.Input.Text("to"), Text = context.Input.Text("text") },
                context.CancellationToken);

            context.Step("SendAsync completed without error");

            var requests = fake.Requests;
            context.Require(
                "Total attempt count matches expectations",
                requests.Count == failCount + 1,
                $"{requests.Count} attempts (expected {failCount + 1})");

            context.Check("The final attempt got a successful response", requests[^1].ResponseStatus == 200);

            context.Artifact("attempts", requests.Select(request => new
            {
                sequence = request.Sequence,
                receivedAt = request.ReceivedAt,
                deltaSincePreviousMs = request.DeltaMs,
                response = request.ResponseStatus,
            }).ToArray(), "table");
        });

    private static Scenario RetryExhausted() => new(
        new ScenarioDescriptor
        {
            Id = "sms.retry-exhausted",
            PackageId = Package,
            Title = "The error propagates once retries are exhausted",
            Summary = "The fake endpoint returns 500 for every request. The package retries up to "
                      + "RetryCount times and eventually passes the HttpRequestException to the "
                      + "caller — it doesn't swallow it.",
            NegativePath = true,
            Fields =
            [
                ToField,
                new ScenarioField("retryCount", "RetryCount", ScenarioFieldKind.Number, "2"),
                new ScenarioField("retryBaseDelayMs", "RetryBaseDelay (ms)", ScenarioFieldKind.Duration, "50"),
            ],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();
            fake.FailNext(100);

            var retryCount = context.Input.Int("retryCount");

            await using var host = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = "playground";
                    sms.Password = "playground-secret";
                    sms.RetryCount = retryCount;
                    sms.RetryBaseDelay = context.Input.Duration("retryBaseDelayMs");
                });

                SmsSupport.TagOutgoingRequests(services, context.RunId);
            });

            Exception? thrown = null;
            try
            {
                await host.GetRequiredService<ISmsSender>().SendAsync(
                    new SmsMessage { To = context.Input.Text("to"), Text = "always fails" },
                    context.CancellationToken);
            }
            catch (HttpRequestException exception)
            {
                thrown = exception;
            }

            context.Require("HttpRequestException thrown", thrown is not null);
            context.Require(
                "First attempt plus RetryCount retries were made",
                fake.Requests.Count == retryCount + 1,
                $"{fake.Requests.Count} attempts (expected {retryCount + 1})");

            context.Artifact("exception", new { type = thrown!.GetType().FullName, message = thrown.Message });
        });

    private static Scenario BusinessRejectNotRetried() => new(
        new ScenarioDescriptor
        {
            Id = "sms.business-reject",
            PackageId = Package,
            Title = "A NetGSM business rejection is not retried",
            Summary = "HTTP 200 + body '20' is a permanent business error. The package throws "
                      + "NetGsmRejectedException and Polly does not retry — only one request goes out.",
            NegativePath = true,
            Fields = [ToField, new ScenarioField("retryCount", "RetryCount", ScenarioFieldKind.Number, "3")],
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();
            fake.RejectNext(1);

            await using var host = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = "playground";
                    sms.Password = "playground-secret";
                    sms.RetryCount = context.Input.Int("retryCount");
                    sms.RetryBaseDelay = TimeSpan.FromMilliseconds(50);
                });

                SmsSupport.TagOutgoingRequests(services, context.RunId);
            });

            Exception? thrown = null;
            try
            {
                await host.GetRequiredService<ISmsSender>().SendAsync(
                    new SmsMessage { To = context.Input.Text("to"), Text = "will be rejected" },
                    context.CancellationToken);
            }
            catch (NetGsmRejectedException exception)
            {
                thrown = exception;
            }

            context.Require("NetGsmRejectedException thrown", thrown is not null);
            context.Require("Not retried", fake.Requests.Count == 1, $"{fake.Requests.Count}");
            context.Artifact("exception", new { type = thrown!.GetType().FullName, message = thrown.Message });
        });

    private static Scenario Guards() => new(
        new ScenarioDescriptor
        {
            Id = "sms.guards",
            PackageId = Package,
            Title = "AllowNoOp and input validation",
            Summary = "With AllowNoOp=true and an empty ApiUrl, sending is silently skipped. With "
                      + "AllowNoOp=false (the default), an empty ApiUrl fails options validation. "
                      + "A number with no digits throws ArgumentException.",
            NegativePath = true,
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            await using var silent = context.Host(services => services.AddPinqponqSms(sms =>
            {
                sms.ApiUrl = null;
                sms.AllowNoOp = true;
                sms.UserCode = "playground";
                sms.Password = "secret";
            }));

            await silent.GetRequiredService<ISmsSender>()
                .SendAsync(new SmsMessage { To = "+905551112233", Text = "should be ignored" }, context.CancellationToken);

            context.Require("No request went out with AllowNoOp", fake.Requests.Count == 0);

            Exception? optionsFail = null;
            try
            {
                await using var denied = context.Host(services => services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = null;
                    sms.AllowNoOp = false;
                }));

                _ = denied.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmsOptions>>().Value;
            }
            catch (Exception exception)
            {
                optionsFail = exception;
            }

            context.Require("AllowNoOp=false + empty ApiUrl fails options validation", optionsFail is not null);
            context.Check(
                "The error names ApiUrl / AllowNoOp",
                optionsFail!.ToString().Contains("ApiUrl", StringComparison.Ordinal),
                optionsFail.Message);

            await using var valid = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = "playground";
                    sms.Password = "secret";
                });

                SmsSupport.TagOutgoingRequests(services, context.RunId);
            });

            Exception? badPhone = null;
            try
            {
                await valid.GetRequiredService<ISmsSender>()
                    .SendAsync(new SmsMessage { To = "abc", Text = "number with no digits" }, context.CancellationToken);
            }
            catch (ArgumentException exception)
            {
                badPhone = exception;
            }

            context.Require("Number with no digits throws ArgumentException", badPhone is not null);
            context.Require("No request went out for the digit-less number", fake.Requests.Count == 0);
            context.Artifact("exceptions", new
            {
                options = new { type = optionsFail.GetType().FullName, message = optionsFail.Message },
                badPhone = new { type = badPhone!.GetType().FullName, message = badPhone.Message },
            });
        });

    private static Scenario HttpsRequired() => new(
        new ScenarioDescriptor
        {
            Id = "sms.https-required",
            PackageId = Package,
            Title = "An HTTP ApiUrl is rejected",
            Summary = "SmsOptionsValidator requires absolute HTTPS for ApiUrl; an http:// URL fails "
                      + "options validation.",
            NegativePath = true,
        },
        async context =>
        {
            Exception? thrown = null;
            try
            {
                await using var host = context.Host(services => services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = "http://127.0.0.1:5199/fake";
                    sms.UserCode = "playground";
                    sms.Password = "secret";
                }));

                _ = host.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmsOptions>>().Value;
            }
            catch (Exception exception)
            {
                thrown = exception;
            }

            context.Require("Options validation fails", thrown is not null);
            context.Check(
                "The error names HTTPS",
                thrown!.ToString().Contains("HTTPS", StringComparison.OrdinalIgnoreCase),
                thrown.Message);
            context.Artifact("exception", new { type = thrown.GetType().FullName, message = thrown.Message });
        });
}

/// <summary>Shared wiring for the SMS-based scenarios.</summary>
internal static class SmsSupport
{
    /// <summary>
    /// Stamps the package's own named client with the run id and rewrites loopback HTTPS
    /// to HTTP so the in-process fake can satisfy the HTTPS options rule.
    /// </summary>
    public static void TagOutgoingRequests(IServiceCollection services, string runId) =>
        services.AddHttpClient(NetGsmSmsSender.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.Add(RunCorrelation.HeaderName, runId))
            .AddHttpMessageHandler(() => new LoopbackHttpsRewriteHandler());
}

/// <summary>Header used to carry a scenario run id across in-process HTTP calls.</summary>
public static class RunCorrelation
{
    /// <summary>Header name the console sets on its own outgoing requests.</summary>
    public const string HeaderName = "X-Playground-Run";
}
