using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Pinqponq.ErrorHandling;
using Pinqponq.ErrorHandling.DependencyInjection;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Scenarios for <c>Pinqponq.ErrorHandling</c>.
/// </summary>
/// <remarks>
/// The middleware is constructed once when the pipeline is built and captures
/// <c>IOptions&lt;ErrorHandlingOptions&gt;.Value</c> at that moment, so a shared branch in the
/// console could never honour per-run option changes. Each run therefore stands up a
/// throwaway in-memory pipeline with <c>TestServer</c> — the same harness the package's own
/// tests use. A permanently mounted branch at <c>/sandbox/errors</c> stays available for
/// poking at the real wire format with curl.
/// </remarks>
public static class ErrorHandlingScenarios
{
    private const string Package = "Pinqponq.ErrorHandling";

    private static readonly IReadOnlyList<ErrorCase> Cases =
    [
        new("unauthorized", "UnauthorizedAccessException", 401, "unauthorized",
            () => throw new UnauthorizedAccessException("Yetkiniz yok.")),
        new("not-found", "KeyNotFoundException", 404, "not_found",
            () => throw new KeyNotFoundException("Kayıt bulunamadı.")),
        new("argument", "ArgumentException", 400, "bad_request",
            () => throw new ArgumentException("Geçersiz argüman.")),
        new("format", "FormatException", 400, "bad_request",
            () => throw new FormatException("Biçim hatalı.")),
        new("invalid-operation", "InvalidOperationException", 400, "bad_request",
            () => throw new InvalidOperationException("Bu işlem şu anda yapılamaz.")),
        new("not-implemented", "NotImplementedException", 501, "not_implemented",
            () => throw new NotImplementedException("Henüz yazılmadı.")),
        new("timeout", "TimeoutException", 504, "timeout",
            () => throw new TimeoutException("Süre aşıldı.")),
        new("custom-notfound", "WidgetNotFoundException (isim kuralı)", 404, "not_found",
            () => throw new WidgetNotFoundException("Widget bulunamadı.")),
        new("unhandled", "Exception", 500, "internal_error",
            () => throw new InvalidProgramException("Beklenmeyen hata.")),
    ];

    /// <summary>Case identifiers the permanently mounted sandbox branch understands.</summary>
    public static IReadOnlyList<string> CaseNames => [.. Cases.Select(item => item.Id)];

    /// <summary>Throws the exception a sandbox case stands for.</summary>
    public static void Throw(string caseId)
    {
        var match = Cases.FirstOrDefault(item => string.Equals(item.Id, caseId, StringComparison.Ordinal))
                    ?? throw new KeyNotFoundException($"Bilinmeyen vaka: {caseId}");

        match.Throw();
    }

    /// <summary>The expected mapping, so the console can render an expected/actual table.</summary>
    public static IEnumerable<object> Expectations() =>
        Cases.Select(item => new
        {
            id = item.Id,
            exception = item.ExceptionLabel,
            statusCode = item.ExpectedStatus,
            responseCode = item.ExpectedResponseCode,
        });

    public static IEnumerable<Scenario> Create()
    {
        yield return MappingMatrix();
        yield return LogShape();
        yield return Correlation();
    }

    private static Scenario MappingMatrix() => new(
        new ScenarioDescriptor
        {
            Id = "errorhandling.mapping",
            PackageId = Package,
            Title = "Exception → HTTP durum haritası",
            Summary = "Dokuz exception türü gerçek bir ASP.NET Core boru hattından geçirilir. "
                      + "Her satırda beklenen ve gerçekleşen durum kodu ile responseCode "
                      + "karşılaştırılır.",
            TimeoutSeconds = 60,
        },
        async context =>
        {
            await using var pipeline = await BuildPipelineAsync(context, _ => { });
            var client = pipeline.Client;
            var rows = new List<object>();

            foreach (var item in Cases)
            {
                using var response = await client.GetAsync(new Uri($"/throw/{item.Id}", UriKind.Relative), context.CancellationToken);
                var body = await ReadErrorAsync(response, context);

                var ok = (int)response.StatusCode == item.ExpectedStatus
                         && body?.ResponseCode == item.ExpectedResponseCode;

                context.Check(
                    $"{item.ExceptionLabel} → {item.ExpectedStatus} {item.ExpectedResponseCode}",
                    ok,
                    $"gelen {(int)response.StatusCode} {body?.ResponseCode}");

                rows.Add(new
                {
                    exception = item.ExceptionLabel,
                    beklenenDurum = item.ExpectedStatus,
                    gelenDurum = (int)response.StatusCode,
                    beklenenKod = item.ExpectedResponseCode,
                    gelenKod = body?.ResponseCode,
                    mesaj = body?.Message,
                });
            }

            context.Artifact("harita", rows, "table");
        });

    private static Scenario LogShape() => new(
        new ScenarioDescriptor
        {
            Id = "errorhandling.log-shape",
            PackageId = Package,
            Title = "Üretilen log kaydının yapısı",
            Summary = "Paketin iddiası, hataları Pinqloq'un beklediği yapılandırılmış biçimde "
                      + "loglaması. Bu senaryo tek bir hata üretir ve kaydın message template'ini, "
                      + "alan adlarını ve seviyesini birebir doğrular.",
            Fields =
            [
                new ScenarioField("case", "Vaka", ScenarioFieldKind.Enum, "unhandled", null,
                    [.. Cases.Select(item => item.Id)]),
            ],
            TimeoutSeconds = 60,
        },
        async context =>
        {
            var caseId = context.Input.Text("case");
            var expected = Cases.First(item => string.Equals(item.Id, caseId, StringComparison.Ordinal));

            await using var pipeline = await BuildPipelineAsync(context, _ => { });

            using var response = await pipeline.Client.GetAsync(
                new Uri($"/throw/{caseId}", UriKind.Relative),
                context.CancellationToken);

            context.Step($"İstek atıldı, {(int)response.StatusCode} döndü");

            var entry = await context.WaitForLogAsync(
                record => record.Category.Contains("ExceptionHandlingMiddleware", StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));

            context.Require("Middleware bir log kaydı üretti", entry is not null);

            const string ExpectedTemplate =
                "Request failed. {ResponseCode} {StatusCode} {Method} {Path} traceId={TraceId} correlationId={CorrelationId}";

            context.Require(
                "Message template beklenen biçimde",
                entry!.MessageTemplate == ExpectedTemplate,
                entry.MessageTemplate);

            string[] expectedKeys = ["ResponseCode", "StatusCode", "Method", "Path", "TraceId", "CorrelationId"];
            var missing = expectedKeys.Where(key => !entry.State.ContainsKey(key)).ToArray();
            context.Require(
                "Yapılandırılmış alanların hepsi var",
                missing.Length == 0,
                missing.Length == 0 ? null : $"eksik: {string.Join(", ", missing)}");

            var expectedLevel = expected.ExpectedStatus >= 500 ? "Error" : "Warning";
            context.Require(
                $"Seviye {expectedLevel} ({expected.ExpectedStatus} için)",
                entry.Level == expectedLevel,
                entry.Level);

            context.Require("Exception kaydı taşınıyor", entry.Exception is not null);
            context.Check(
                "ResponseCode alanı doğru",
                entry.State["ResponseCode"]?.ToString() == expected.ExpectedResponseCode);

            context.Artifact("ham log kaydı", entry);
        });

    private static Scenario Correlation() => new(
        new ScenarioDescriptor
        {
            Id = "errorhandling.correlation",
            PackageId = Package,
            Title = "Correlation id ve hata mesajı sızdırma",
            Summary = "Gelen X-Correlation-ID başlığı hem yanıt gövdesindeki traceId'ye hem de "
                      + "log kaydındaki CorrelationId alanına yansır — logdaki TraceId ise "
                      + "isteğin kendi kimliğidir, ikisi farklıdır. IncludeExceptionMessage'ın "
                      + "açık/kapalı farkı da gösterilir.",
            Fields =
            [
                new ScenarioField("correlationId", "X-Correlation-ID", ScenarioFieldKind.Text, "pinq-12345"),
                new ScenarioField("headerName", "CorrelationIdHeader", ScenarioFieldKind.Text, "X-Correlation-ID"),
            ],
            TimeoutSeconds = 60,
        },
        async context =>
        {
            var correlationId = context.Input.Text("correlationId");
            var headerName = context.Input.Text("headerName");

            await using var hidden = await BuildPipelineAsync(context, options =>
            {
                options.CorrelationIdHeader = headerName;
                options.IncludeExceptionMessage = false;
            });

            using var hiddenRequest = new HttpRequestMessage(HttpMethod.Get, "/throw/unhandled");
            hiddenRequest.Headers.Add(headerName, correlationId);
            using var hiddenResponse = await hidden.Client.SendAsync(hiddenRequest, context.CancellationToken);
            var hiddenBody = await ReadErrorAsync(hiddenResponse, context);

            context.Require("Yanıt traceId'si gönderilen correlation id", hiddenBody?.TraceId == correlationId,
                hiddenBody?.TraceId);
            context.Require(
                "IncludeExceptionMessage kapalıyken iç mesaj sızmıyor",
                hiddenBody?.Message?.Contains("Beklenmeyen hata", StringComparison.Ordinal) != true,
                hiddenBody?.Message);
            context.Artifact("gövde (mesaj gizli)", hiddenBody);

            var entry = await context.WaitForLogAsync(
                record => record.Category.Contains("ExceptionHandlingMiddleware", StringComparison.Ordinal)
                          && record.State.TryGetValue("CorrelationId", out var value)
                          && string.Equals(value?.ToString(), correlationId, StringComparison.Ordinal),
                TimeSpan.FromSeconds(10));

            context.Require("Log kaydında correlation id var", entry is not null);
            var loggedTraceId = entry!.State["TraceId"]?.ToString();
            context.Check(
                "Logdaki TraceId isteğin kendi kimliği, correlation id'den farklı",
                !string.Equals(loggedTraceId, correlationId, StringComparison.Ordinal),
                $"TraceId={loggedTraceId}");

            await using var revealed = await BuildPipelineAsync(context, options =>
            {
                options.CorrelationIdHeader = headerName;
                options.IncludeExceptionMessage = true;
            });

            using var revealedResponse = await revealed.Client.GetAsync(
                new Uri("/throw/unhandled", UriKind.Relative),
                context.CancellationToken);
            var revealedBody = await ReadErrorAsync(revealedResponse, context);

            context.Require(
                "IncludeExceptionMessage açıkken gerçek mesaj dönüyor",
                revealedBody?.Message?.Contains("Beklenmeyen hata", StringComparison.Ordinal) == true,
                revealedBody?.Message);

            context.Artifact("gövde (mesaj açık)", revealedBody);
            context.Artifact("log alanları", entry.State);
        });

    private static async Task<SandboxPipeline> BuildPipelineAsync(
        ScenarioContext context,
        Action<ErrorHandlingOptions> configure)
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddLogging(logging =>
                    {
                        logging.ClearProviders();
                        logging.SetMinimumLevel(LogLevel.Trace);
                        // Host startup chatter would drown out the middleware record this
                        // scenario exists to show.
                        logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
                        logging.AddProvider(context.CreateLogProvider());
                    });

                    services.AddPinqponqErrorHandling(configure);
                });

                web.Configure(app =>
                {
                    app.UsePinqponqErrorHandling();
                    app.Run(httpContext =>
                    {
                        var caseId = httpContext.Request.Path.Value?.TrimStart('/').Replace("throw/", string.Empty, StringComparison.Ordinal)
                                     ?? string.Empty;
                        Throw(caseId);
                        return Task.CompletedTask;
                    });
                });
            })
            .StartAsync(context.CancellationToken);

        return new SandboxPipeline(host, host.GetTestClient());
    }

    private static async Task<ErrorResponse?> ReadErrorAsync(HttpResponseMessage response, ScenarioContext context)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                context.CancellationToken);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record ErrorCase(
        string Id,
        string ExceptionLabel,
        int ExpectedStatus,
        string ExpectedResponseCode,
        Action Throw);

    private sealed class SandboxPipeline(IHost host, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await host.StopAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            host.Dispose();
        }
    }
}

/// <summary>
/// Exists only to demonstrate the mapping rule that routes any exception whose type name
/// contains "NotFound" to HTTP 404.
/// </summary>
public sealed class WidgetNotFoundException(string message) : Exception(message);
