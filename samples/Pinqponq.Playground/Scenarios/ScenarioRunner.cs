using System.Diagnostics;
using Microsoft.Extensions.Options;
using Pinqponq.Playground.Diagnostics;
using Pinqponq.Playground.Infrastructure;

namespace Pinqponq.Playground.Scenarios;

/// <summary>
/// Executes a scenario and returns its outcome together with the log entries it produced.
/// </summary>
public sealed class ScenarioRunner(
    ScenarioCatalog catalog,
    DevStackManager stack,
    LogSink sink,
    IOptions<PlaygroundOptions> options,
    ILoggerFactory loggerFactory,
    IServiceProvider appServices)
{
    private readonly PlaygroundOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Runs one scenario. Never throws for scenario failures — they become results.</summary>
    public async Task<ScenarioRunResult> RunAsync(
        string scenarioId,
        IReadOnlyDictionary<string, string?> input,
        Uri baseAddress,
        CancellationToken cancellationToken)
    {
        var scenario = catalog.Get(scenarioId);
        var descriptor = scenario.Descriptor;
        var runId = $"r_{Guid.NewGuid():N}"[..10];

        var timeout = TimeSpan.FromSeconds(
            descriptor.TimeoutSeconds > 0 ? descriptor.TimeoutSeconds : _options.ScenarioTimeoutSeconds);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);

        // The ambient id covers logs raised outside the run's own provider — most
        // importantly the error-handling middleware running in the sandbox branch.
        using var ambient = ScenarioRunContext.Begin(runId);

        var context = new ScenarioContext(
            runId,
            descriptor,
            new ScenarioInputs(descriptor, input),
            stack,
            sink,
            baseAddress,
            loggerFactory.CreateLogger($"Playground.Scenario.{descriptor.Id}"),
            appServices,
            linked.Token);

        string status = "passed";
        string? error = null;
        string? errorType = null;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            foreach (var required in descriptor.RequiredServices)
            {
                if (!stack.IsReady(required))
                {
                    var service = stack.Get(required);
                    throw new ScenarioSkippedException(
                        $"'{service.DisplayName}' hazır değil ({Translate(service.State)}). Üst şeritten başlatıp tekrar deneyin.");
                }
            }

            await scenario.RunAsync(context).ConfigureAwait(false);
        }
        catch (ScenarioSkippedException exception)
        {
            status = "skipped";
            error = exception.Message;
        }
        catch (DevStackNotReadyException exception)
        {
            status = "skipped";
            error = exception.Message;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            status = "failed";
            error = $"Senaryo {timeout.TotalSeconds:0} saniye içinde tamamlanmadı.";
            errorType = "Timeout";
        }
        catch (Exception exception)
        {
            status = "failed";
            error = exception.Message;
            errorType = exception.GetType().FullName;
        }

        stopwatch.Stop();

        // Some entries are written just after the awaited call returns (broker acks,
        // container teardown); a short settle keeps them in this run's log list.
        await Task.Delay(80, CancellationToken.None).ConfigureAwait(false);

        if (status == "passed" && context.Steps.Any(step => !step.Ok))
        {
            status = "failed";
            error ??= "Bir veya daha fazla kontrol başarısız.";
        }

        return new ScenarioRunResult
        {
            RunId = runId,
            ScenarioId = descriptor.Id,
            Success = status == "passed",
            Status = status,
            Error = error,
            ErrorType = errorType,
            DurationMs = stopwatch.ElapsedMilliseconds,
            Steps = context.Steps,
            Artifacts = context.Artifacts,
            Logs = sink.Query(new LogQuery { RunId = runId, Take = 2000 }),
        };
    }

    private static string Translate(DevServiceState state) => state switch
    {
        DevServiceState.Stopped => "durdu",
        DevServiceState.Starting => "başlatılıyor",
        DevServiceState.Ready => "hazır",
        DevServiceState.Failed => "hata",
        DevServiceState.DockerUnavailable => "Docker yok",
        DevServiceState.External => "harici",
        _ => state.ToString(),
    };
}
