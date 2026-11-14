using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Pinqponq.OpenApi;

internal sealed class DocumentInfoTransformer(IOptions<PinqponqOpenApiOptions> openApiOptions) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var options = openApiOptions.Value;
        document.Info ??= new OpenApiInfo();

        if (options.Title is not null)
        {
            document.Info.Title = options.Title;
        }

        if (options.Description is not null)
        {
            document.Info.Description = options.Description;
        }

        return Task.CompletedTask;
    }
}
