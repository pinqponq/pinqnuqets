using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Pinqponq.OpenApi;

internal sealed class CustomizerTransformer(IEnumerable<IPinqponqOpenApiCustomizer> customizers) : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var customizer in customizers)
        {
            await customizer.CustomizeDocumentAsync(document, context, cancellationToken);
        }
    }

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        foreach (var customizer in customizers)
        {
            await customizer.CustomizeOperationAsync(operation, context, cancellationToken);
        }
    }
}
