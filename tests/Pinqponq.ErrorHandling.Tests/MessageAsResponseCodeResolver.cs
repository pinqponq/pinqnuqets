namespace Pinqponq.ErrorHandling.Tests;

// Answers with the exception's message as the response code and the built-in status, so tests can pick any catalog code.
internal sealed class MessageAsResponseCodeResolver : IExceptionResolver
{
    public ExceptionResolution? Resolve(Exception exception) =>
        new(ExceptionMapping.Map(exception).StatusCode, exception.Message);
}
