using Pinqloq;

namespace Pinqponq.Observability.ClientLogs;

internal static class ClientLogLevels
{
    private const string DebugLevel = "DEBUG";
    private const string WarningLevel = "WARNING";
    private const string WarnLevel = "WARN";
    private const string ErrorLevel = "ERROR";
    private const string FatalLevel = "FATAL";

    public static PinqloqLogLevel Parse(string? logLevel)
    {
        return logLevel?.ToUpperInvariant() switch
        {
            DebugLevel => PinqloqLogLevel.Debug,
            WarningLevel or WarnLevel => PinqloqLogLevel.Warning,
            ErrorLevel => PinqloqLogLevel.Error,
            FatalLevel => PinqloqLogLevel.Fatal,
            _ => PinqloqLogLevel.Information
        };
    }
}
