namespace Pinqponq.ErrorHandling;

/// <summary>
/// Resolves exceptions of a specific technology (e.g. a database driver) before the
/// <see cref="ErrorHandlingOptions.StatusMappingResolver"/> and the built-in mapping run.
/// Register implementations in DI as <see cref="IExceptionResolver"/>; the first one that
/// returns a resolution wins.
/// </summary>
public interface IExceptionResolver
{
    /// <summary>Returns the resolution, or <see langword="null"/> when the exception is not handled here.</summary>
    ExceptionResolution? Resolve(Exception exception);
}
