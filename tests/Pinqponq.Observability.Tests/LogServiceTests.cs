using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqloq;
using Xunit;

namespace Pinqponq.Observability.Tests;

public sealed class LogServiceTests
{
    private readonly CapturingPinqloqLogger _pinqloqLogger = new();
    private readonly CapturingLogger<LogService> _logger = new();

    private LogService CreateLogService() =>
        new(_logger, _pinqloqLogger, Options.Create(new ObservabilityOptions { JobsCollectionName = "app_jobs" }));

    [Fact]
    public void Record_without_collection_goes_to_the_jobs_collection_with_the_message_as_event()
    {
        CreateLogService().Info("Nightly cleanup finished");

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal("Nightly cleanup finished", entry.Event);
        Assert.Equal("app_jobs", entry.CollectionName);
        Assert.Equal(PinqloqLogLevel.Information, entry.LogLevel);
        Assert.Equal(PinqloqLogSourceType.Backend, entry.LogSourceType);
        Assert.Equal(string.Empty, entry.DeviceIdentifier);
        Assert.Null(entry.Metadata);
        Assert.Null(entry.Detail);
        Assert.Null(entry.Path);
    }

    [Fact]
    public void Data_becomes_metadata_and_detail_with_non_strings_as_camel_case_json()
    {
        var data = new Dictionary<string, object?>
        {
            ["orderId"] = "42",
            ["count"] = 3,
            ["filter"] = new { PageSize = 10 },
            ["skipped"] = null,
        };

        CreateLogService().Warn("Export slow", data, "app_http", "export_slow", "device-1", correlationId: "corr-1");

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal("export_slow", entry.Event);
        Assert.Equal("app_http", entry.CollectionName);
        Assert.Equal("device-1", entry.DeviceIdentifier);
        Assert.Equal("corr-1", entry.CorrelationId);
        Assert.Equal(PinqloqLogLevel.Warning, entry.LogLevel);
        var expectedFields = new Dictionary<string, string> { ["orderId"] = "42", ["count"] = "3", ["filter"] = """{"pageSize":10}""" };
        Assert.Equal(expectedFields, entry.Metadata);
        Assert.Equal(expectedFields, entry.Detail);
        Assert.NotSame(entry.Metadata, entry.Detail);
    }

    [Fact]
    public void Path_field_moves_out_of_the_fields_and_an_explicit_path_wins()
    {
        var data = new Dictionary<string, object?> { ["path"] = "/api/orders", ["orderId"] = "42" };

        CreateLogService().Info("from data", data);
        CreateLogService().Debug("explicit", data, path: "/api/explicit");

        Assert.Equal("/api/orders", _pinqloqLogger.Entries[0].Path);
        Assert.Equal("/api/explicit", _pinqloqLogger.Entries[1].Path);
        Assert.All(_pinqloqLogger.Entries, entry =>
        {
            Assert.False(entry.Metadata!.ContainsKey("path"));
            Assert.False(entry.Detail!.ContainsKey("path"));
        });
        Assert.Equal(PinqloqLogLevel.Debug, _pinqloqLogger.Entries[1].LogLevel);
    }

    [Fact]
    public void Data_other_than_a_field_dictionary_adds_no_fields()
    {
        CreateLogService().Info("anonymous data", new { OrderId = 42 });

        Assert.Null(Assert.Single(_pinqloqLogger.Entries).Metadata);
    }

    [Fact]
    public void Exception_adds_type_message_stack_trace_and_inner_exception()
    {
        var exception = CaptureThrown(() => throw new InvalidOperationException("outer", new TimeoutException("inner")));

        CreateLogService().Error("Export failed", exception);

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal(PinqloqLogLevel.Error, entry.LogLevel);
        Assert.Equal("System.InvalidOperationException", entry.Detail!["exceptionType"]);
        Assert.Equal("outer", entry.Detail["exceptionMessage"]);
        Assert.Contains(nameof(Exception_adds_type_message_stack_trace_and_inner_exception), entry.Detail["stackTrace"]);
        Assert.Equal("System.TimeoutException", entry.Detail["innerExceptionType"]);
        Assert.Equal("inner", entry.Detail["innerExceptionMessage"]);
        Assert.Equal(entry.Detail, entry.Metadata);
    }

    [Fact]
    public void Exception_without_stack_trace_or_inner_exception_adds_only_type_and_message()
    {
        CreateLogService().Warn("Retrying", new TimeoutException("slow"));

        var entry = Assert.Single(_pinqloqLogger.Entries);
        Assert.Equal(["exceptionType", "exceptionMessage"], entry.Detail!.Keys.ToArray());
        Assert.Equal(PinqloqLogLevel.Warning, entry.LogLevel);
    }

    [Fact]
    public void Long_stack_trace_is_trimmed_to_4000_characters()
    {
        var exception = new ExceptionWithStackTrace(new string('x', 4001));

        CreateLogService().Error("Deep failure", exception);

        Assert.Equal(new string('x', 4000), Assert.Single(_pinqloqLogger.Entries).Detail!["stackTrace"]);
    }

    [Fact]
    public void Stack_trace_of_exactly_4000_characters_is_kept()
    {
        var exception = new ExceptionWithStackTrace(new string('x', 4000));

        CreateLogService().Error("Deep failure", exception);

        Assert.Equal(4000, Assert.Single(_pinqloqLogger.Entries).Detail!["stackTrace"].Length);
    }

    [Fact]
    public void Aggregate_exception_reports_its_count_and_first_failure()
    {
        var aggregateException = new AggregateException(new TimeoutException("first"), new AggregateException(new ArgumentException("second")));

        CreateLogService().Error("Parallel work failed", aggregateException);

        var detail = Assert.Single(_pinqloqLogger.Entries).Detail!;
        Assert.Equal("2", detail["aggregatedExceptionCount"]);
        Assert.Equal("System.TimeoutException", detail["exceptionType"]);
        Assert.Equal("first", detail["exceptionMessage"]);
    }

    [Fact]
    public void Empty_aggregate_exception_reports_itself()
    {
        CreateLogService().Error("Nothing inside", new AggregateException());

        var detail = Assert.Single(_pinqloqLogger.Entries).Detail!;
        Assert.Equal("0", detail["aggregatedExceptionCount"]);
        Assert.Equal("System.AggregateException", detail["exceptionType"]);
    }

    [Fact]
    public void Every_record_is_also_written_to_the_logger_at_its_level()
    {
        var logService = CreateLogService();

        logService.Debug("d");
        logService.Info("i", new Dictionary<string, object?> { ["orderId"] = "42" });
        logService.Warn("w", (object?)null);
        logService.Warn("we", new TimeoutException());
        logService.Error("e");

        Assert.Equal(
            [LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Warning, LogLevel.Error],
            _logger.Records.Select(record => record.Level).ToArray());
        Assert.Equal(
            ["d (null)", """i {"orderId":"42"}""", "w (null)", "we (null)", "e (null)"],
            _logger.Records.Select(record => record.Message).ToArray());
        Assert.IsType<TimeoutException>(_logger.Records[3].Exception);
    }

    [Fact]
    public void Delivery_failure_is_logged_as_a_warning()
    {
        _pinqloqLogger.FailureToReport = new PinqloqLogError(default, 503, "unavailable", null!);

        CreateLogService().Info("Nightly cleanup finished");

        Assert.NotEmpty(_logger.Records);
        var failureRecord = _logger.Records[^1];
        Assert.Equal(LogLevel.Warning, failureRecord.Level);
        Assert.Contains("pinqloq delivery failed", failureRecord.Message);
        Assert.Contains("503", failureRecord.Message);
        Assert.Contains("unavailable", failureRecord.Message);
    }

    private static Exception CaptureThrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        throw new InvalidOperationException("Nothing was thrown.");
    }

    private sealed class ExceptionWithStackTrace(string stackTrace) : Exception("deep")
    {
        public override string? StackTrace { get; } = stackTrace;
    }
}
