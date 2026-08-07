using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using Pinqponq.Playground.Diagnostics;
using Pinqponq.Playground.Infrastructure;

namespace Pinqponq.Playground.Scenarios;

/// <summary>Raised when a scenario's expectation does not hold.</summary>
public sealed class ScenarioAssertionException(string message) : Exception(message);

/// <summary>Raised when a scenario cannot run for an environmental reason, not a defect.</summary>
public sealed class ScenarioSkippedException(string message) : Exception(message);

/// <summary>
/// Everything a scenario body needs: typed inputs, the dev stack, a run-scoped logger and
/// the ability to build isolated service providers.
/// </summary>
public sealed class ScenarioContext
{
    private readonly LogSink _sink;
    private readonly List<ScenarioStep> _steps = [];
    private readonly List<ScenarioArtifact> _artifacts = [];
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private long _lastStepAt;

    internal ScenarioContext(
        string runId,
        ScenarioDescriptor descriptor,
        ScenarioInputs input,
        DevStackManager stack,
        LogSink sink,
        Uri baseAddress,
        ILogger logger,
        IServiceProvider appServices,
        CancellationToken cancellationToken)
    {
        RunId = runId;
        Descriptor = descriptor;
        Input = input;
        Stack = stack;
        _sink = sink;
        BaseAddress = baseAddress;
        Logger = logger;
        AppServices = appServices;
        CancellationToken = cancellationToken;
    }

    /// <summary>Correlates this run with the log entries it produces.</summary>
    public string RunId { get; }

    public ScenarioDescriptor Descriptor { get; }

    /// <summary>Values entered in the console, falling back to the field defaults.</summary>
    public ScenarioInputs Input { get; }

    public DevStackManager Stack { get; }

    /// <summary>The console's own base address, used for the in-process fakes and sandbox.</summary>
    public Uri BaseAddress { get; }

    /// <summary>Logger whose entries carry this run's id.</summary>
    public ILogger Logger { get; }

    /// <summary>
    /// The console's own services — for the in-process fakes (recorded SMS traffic, the
    /// MailHog client) which live outside the run's isolated container.
    /// </summary>
    public IServiceProvider AppServices { get; }

    public CancellationToken CancellationToken { get; }

    /// <summary>
    /// HTTPS URL of the built-in fake NetGSM GET endpoint.
    /// </summary>
    /// <remarks>
    /// <c>SmsOptionsValidator</c> requires HTTPS. The console listens on HTTP only; the
    /// named client's <see cref="Infrastructure.LoopbackHttpsRewriteHandler"/> rewrites
    /// loopback HTTPS back to HTTP before the request leaves the process.
    /// </remarks>
    public string FakeSmsUrl => LoopbackHttps(BaseAddress, "/fake-netgsm/sms/send/get");

    /// <summary>HTTPS URL of the built-in fake NetGSM RestV2 POST endpoint.</summary>
    public string FakeSmsRestV2Url => LoopbackHttps(BaseAddress, "/fake-netgsm/sms/rest/v2/send");

    private static string LoopbackHttps(Uri baseAddress, string path)
    {
        var builder = new UriBuilder(baseAddress)
        {
            Scheme = Uri.UriSchemeHttps,
            Path = path,
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri.ToString();
    }

    internal IReadOnlyList<ScenarioStep> Steps => _steps;

    internal IReadOnlyList<ScenarioArtifact> Artifacts => _artifacts;

    /// <summary>Records a completed step.</summary>
    public void Step(string title, string? detail = null) => Record(title, ok: true, detail);

    /// <summary>Records a check and returns whether it held.</summary>
    public bool Check(string title, bool condition, string? detail = null)
    {
        Record(title, condition, detail);
        return condition;
    }

    /// <summary>Records a check and aborts the run when it does not hold.</summary>
    public void Require(string title, bool condition, string? detail = null)
    {
        Record(title, condition, detail);
        if (!condition)
        {
            throw new ScenarioAssertionException($"{title}{(detail is null ? string.Empty : $" — {detail}")}");
        }
    }

    /// <summary>Records a value for display: JWTs, JSON payloads, tables, URIs.</summary>
    public void Artifact(string name, object? value, string kind = "json") =>
        _artifacts.Add(new ScenarioArtifact(name, kind, value));

    /// <summary>Ends the run as skipped rather than failed — used for environmental limits.</summary>
    public static void Skip(string reason) => throw new ScenarioSkippedException(reason);

    /// <summary>
    /// Builds an isolated service provider for this run. This is where the package's own
    /// <c>AddPinqponqXxx</c> extension is invoked, so registration is under test too.
    /// </summary>
    public ScenarioHost Host(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddProvider(new CapturingLoggerProvider(_sink, RunId));
        });

        configure(services);

        // ASP0000: building a provider from a collection is normally a mistake in a web app.
        // Here it is the entire point — every run gets a throwaway container so option
        // changes take effect and no singleton state survives between runs.
#pragma warning disable ASP0000
        // ValidateScopes catches lifetime mistakes; ValidateOnBuild is deliberately off, so a
        // scenario can demonstrate a genuinely missing registration at the resolve call where
        // the error is specific, instead of failing every unrelated scenario at build.
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });
#pragma warning restore ASP0000

        return new ScenarioHost(provider);
    }

    /// <summary>An <see cref="HttpClient"/> pointed at the console itself.</summary>
    public HttpClient CreateHttpClient() => new() { BaseAddress = BaseAddress, Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// A logger provider stamped with this run's id, for scenarios that build their own
    /// host rather than a plain service collection.
    /// </summary>
    public ILoggerProvider CreateLogProvider() => new CapturingLoggerProvider(_sink, RunId);

    /// <summary>
    /// Waits for a log entry from this run that matches <paramref name="match"/>.
    /// </summary>
    /// <remarks>
    /// Packages that log from a background thread — the RabbitMQ consumer routing a failed
    /// message to the dead-letter exchange, for instance — emit after the call that
    /// triggered them has already returned.
    /// </remarks>
    public async Task<LogRecord?> WaitForLogAsync(Func<LogRecord, bool> match, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(match);

        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var found = _sink.Query(new LogQuery { RunId = RunId, Take = 2000 }).FirstOrDefault(match);
            if (found is not null)
            {
                return found;
            }

            await Task.Delay(100, CancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private void Record(string title, bool ok, string? detail)
    {
        var now = _stopwatch.ElapsedMilliseconds;
        _steps.Add(new ScenarioStep(_steps.Count, title, ok, detail, now - _lastStepAt));
        _lastStepAt = now;
    }
}

/// <summary>
/// An isolated per-run service provider, including any hosted services the package
/// registered.
/// </summary>
public sealed class ScenarioHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;
    private readonly List<IHostedService> _started = [];

    internal ScenarioHost(ServiceProvider provider)
    {
        _provider = provider;
        _scope = provider.CreateAsyncScope();
    }

    /// <summary>Scoped provider — resolve everything through this.</summary>
    public IServiceProvider Services => _scope.ServiceProvider;

    /// <summary>Resolves a required service from the run's scope.</summary>
    public T GetRequiredService<T>()
        where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>
    /// Starts hosted services the package registered — a bare provider never does this on
    /// its own, so the RabbitMQ consumer would silently never consume.
    /// </summary>
    public async Task StartHostedServicesAsync(CancellationToken cancellationToken)
    {
        foreach (var hosted in _provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(cancellationToken).ConfigureAwait(false);
            _started.Add(hosted);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Consumers must stop before the connection they use is disposed.
        for (var i = _started.Count - 1; i >= 0; i--)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _started[i].StopAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Shutdown races are not interesting; the run result already carries the outcome.
            }
        }

        await _scope.DisposeAsync().ConfigureAwait(false);

        // Async disposal is mandatory: IRabbitMqConnection implements only IAsyncDisposable,
        // and a synchronous Dispose on a provider owning one throws.
        await _provider.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>Typed access to the values the console submitted, with descriptor defaults.</summary>
public sealed class ScenarioInputs(ScenarioDescriptor descriptor, IReadOnlyDictionary<string, string?> values)
{
    /// <summary>Raw values as submitted, for echoing back in the result.</summary>
    public IReadOnlyDictionary<string, string?> Raw => values;

    /// <summary>Required non-empty text.</summary>
    public string Text(string name) =>
        Resolve(name) ?? throw new ScenarioAssertionException($"'{name}' is required.");

    /// <summary>Optional text; null when neither a value nor a default is present.</summary>
    public string? TextOrNull(string name) => Resolve(name);

    public int Int(string name) =>
        int.TryParse(Resolve(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ScenarioAssertionException($"'{name}' must be numeric.");

    public bool Bool(string name) =>
        Resolve(name) is { } raw && bool.TryParse(raw, out var value) && value;

    /// <summary>A duration expressed in milliseconds.</summary>
    public TimeSpan Duration(string name) => TimeSpan.FromMilliseconds(Int(name));

    public TEnum Enum<TEnum>(string name)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(Resolve(name), ignoreCase: true, out var value)
            ? value
            : throw new ScenarioAssertionException($"'{name}' is not a valid option.");

    private string? Resolve(string name)
    {
        if (values.TryGetValue(name, out var submitted) && !string.IsNullOrWhiteSpace(submitted))
        {
            return submitted;
        }

        var field = descriptor.Fields.FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(field?.Default) ? null : field.Default;
    }
}
