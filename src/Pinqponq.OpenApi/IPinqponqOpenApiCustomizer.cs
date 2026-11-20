using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pinqponq.OpenApi;

/// <summary>
/// Changes the document after the package has built it. Register any number of implementations; they run in
/// registration order, after the package's own changes. Implement only the method you need.
/// </summary>
public interface IPinqponqOpenApiCustomizer
{
    /// <summary>Changes the whole document, for example its tags or extra components.</summary>
    Task CustomizeDocumentAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Changes one operation, for example its description or extra responses.</summary>
    Task CustomizeOperationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
