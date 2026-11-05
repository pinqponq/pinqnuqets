using Microsoft.AspNetCore.Http;
using Pinqloq;

namespace Pinqponq.Observability.ClientLogs;

/// <summary>
/// Adds or replaces product fields on a client log before it is written, for example the signed-in user's id.
/// Register it in DI (any lifetime); every registered enricher is called once per entry, after the request was
/// checked and redacted and before any entry of the request is written. Values set here are not redacted or cut.
/// </summary>
public interface IClientLogEnricher
{
    /// <summary>Changes <paramref name="entry"/> for the request in <paramref name="httpContext"/>.</summary>
    void Enrich(HttpContext httpContext, PinqloqLogEntry entry);
}
