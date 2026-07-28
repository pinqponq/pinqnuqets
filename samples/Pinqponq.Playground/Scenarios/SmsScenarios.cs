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
        new("to", "Alıcı", ScenarioFieldKind.Text, "+90 555 111 22 33");

    private static readonly ScenarioField TextField =
        new("text", "Mesaj", ScenarioFieldKind.Text, "Pinqponq test mesajı");

    public static IEnumerable<Scenario> Create()
    {
        yield return SendHappyPath();
        yield return RetryThenSuccess();
        yield return RetryExhausted();
        yield return Guards();
    }

    private static Scenario SendHappyPath() => new(
        new ScenarioDescriptor
        {
            Id = "sms.send",
            PackageId = Package,
            Title = "SMS gönder",
            Summary = "Konsolun içindeki sahte NetGSM ucuna gerçek bir HTTP isteği atar ve "
                      + "paketin kurduğu sorgu parametrelerini olduğu gibi gösterir.",
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

            context.Step("SendAsync tamamlandı");

            var requests = fake.Requests;
            context.Require("Sahte uç tam olarak bir istek aldı", requests.Count == 1, $"{requests.Count} istek");

            var request = requests[0];
            context.Check(
                "Telefon numarası yalnızca rakamlara indirgenmiş",
                request.GsmNo?.All(char.IsDigit) == true,
                request.GsmNo);
            context.Check("Mesaj metni korunmuş", request.Message == context.Input.Text("text"), request.Message);
            context.Check("MsgHeader gönderilmiş", request.MsgHeader == context.Input.Text("msgHeader"));

            context.Artifact("alınan istek", new
            {
                gsmno = request.GsmNo,
                message = request.Message,
                msgheader = request.MsgHeader,
                usercode = request.UserCode,
                rawQuery = request.RawQuery,
            });
        });

    private static Scenario RetryThenSuccess() => new(
        new ScenarioDescriptor
        {
            Id = "sms.retry",
            PackageId = Package,
            Title = "Geçici hatada tekrar dener",
            Summary = "Sahte uca ilk N isteği 500 ile yanıtlaması söylenir. Paketin Polly "
                      + "boru hattı denemeleri tekrarlar; istekler arası gecikmelerden üstel "
                      + "geri çekilme (jitter'lı) doğrudan görülür.",
            Fields =
            [
                ToField,
                TextField,
                new ScenarioField("failCount", "Kaç istek 500 dönsün", ScenarioFieldKind.Number, "2"),
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
            context.Step($"Sahte uç ilk {failCount} isteği 500 dönecek şekilde ayarlandı");

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

            context.Step("SendAsync hatasız tamamlandı");

            var requests = fake.Requests;
            context.Require(
                "Toplam deneme sayısı beklendiği gibi",
                requests.Count == failCount + 1,
                $"{requests.Count} deneme (beklenen {failCount + 1})");

            context.Check(
                "Son deneme başarıyla yanıtlandı",
                requests[^1].ResponseStatus == 200);

            context.Artifact("denemeler", requests.Select(request => new
            {
                sira = request.Sequence,
                zaman = request.ReceivedAt,
                oncekindenBuYanaMs = request.DeltaMs,
                yanit = request.ResponseStatus,
            }).ToArray(), "table");
        });

    private static Scenario RetryExhausted() => new(
        new ScenarioDescriptor
        {
            Id = "sms.retry-exhausted",
            PackageId = Package,
            Title = "Denemeler tükenince hata yükselir",
            Summary = "Sahte uç her isteği 500 döner. Paket RetryCount kadar tekrar dener ve "
                      + "sonunda HttpRequestException'ı çağırana geçirir — hatayı yutmaz.",
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
                    new SmsMessage { To = context.Input.Text("to"), Text = "hep hata" },
                    context.CancellationToken);
            }
            catch (HttpRequestException exception)
            {
                thrown = exception;
            }

            context.Require("HttpRequestException yükseldi", thrown is not null);
            context.Require(
                "İlk deneme + RetryCount kadar tekrar yapıldı",
                fake.Requests.Count == retryCount + 1,
                $"{fake.Requests.Count} deneme (beklenen {retryCount + 1})");

            context.Artifact("exception", new { type = thrown!.GetType().FullName, message = thrown.Message });
        });

    private static Scenario Guards() => new(
        new ScenarioDescriptor
        {
            Id = "sms.guards",
            PackageId = Package,
            Title = "Yapılandırma ve girdi kontrolleri",
            Summary = "ApiUrl boşken gönderim sessizce atlanır (geliştirme modu); UserCode "
                      + "eksikken anlaşılır bir hata verir; rakam içermeyen numara sessizce atlanır.",
            NegativePath = true,
        },
        async context =>
        {
            var fake = context.AppServices.GetRequiredService<FakeNetGsmState>();
            fake.Reset();

            await using var silent = context.Host(services => services.AddPinqponqSms(sms =>
            {
                sms.ApiUrl = null;
                sms.UserCode = "playground";
                sms.Password = "secret";
            }));

            await silent.GetRequiredService<ISmsSender>()
                .SendAsync(new SmsMessage { To = "+905551112233", Text = "yok sayılmalı" }, context.CancellationToken);

            context.Require("ApiUrl boşken hiç istek gitmedi", fake.Requests.Count == 0);

            await using var missingCredentials = context.Host(services =>
            {
                services.AddPinqponqSms(sms =>
                {
                    sms.ApiUrl = context.FakeSmsUrl;
                    sms.UserCode = null;
                    sms.Password = "secret";
                });

                SmsSupport.TagOutgoingRequests(services, context.RunId);
            });

            Exception? thrown = null;
            try
            {
                await missingCredentials.GetRequiredService<ISmsSender>()
                    .SendAsync(new SmsMessage { To = "+905551112233", Text = "kimlik yok" }, context.CancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                thrown = exception;
            }

            context.Require("UserCode eksikken InvalidOperationException", thrown is not null);
            context.Check("Hata mesajı alanı adıyla söylüyor",
                thrown!.Message.Contains("UserCode", StringComparison.Ordinal), thrown.Message);

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

            await valid.GetRequiredService<ISmsSender>()
                .SendAsync(new SmsMessage { To = "abc", Text = "rakamsız numara" }, context.CancellationToken);

            context.Require("Rakam içermeyen numarada istek atılmadı", fake.Requests.Count == 0);
            context.Artifact("exception", new { type = thrown.GetType().FullName, message = thrown.Message });
        });
}

/// <summary>Shared wiring for the SMS-based scenarios.</summary>
internal static class SmsSupport
{
    /// <summary>
    /// Stamps the package's own named client with the run id.
    /// </summary>
    /// <remarks>
    /// The fake endpoint is reached over real HTTP, so the run's ambient correlation does
    /// not flow into the request. Adding a header does — and <c>AddHttpClient</c>
    /// configuration is additive, so this augments the package's registration instead of
    /// replacing it.
    /// </remarks>
    public static void TagOutgoingRequests(IServiceCollection services, string runId) =>
        services.AddHttpClient(NetGsmSmsSender.HttpClientName)
            .ConfigureHttpClient(client => client.DefaultRequestHeaders.Add(RunCorrelation.HeaderName, runId));
}

/// <summary>Header used to carry a scenario run id across in-process HTTP calls.</summary>
public static class RunCorrelation
{
    /// <summary>Header name the console sets on its own outgoing requests.</summary>
    public const string HeaderName = "X-Playground-Run";
}
