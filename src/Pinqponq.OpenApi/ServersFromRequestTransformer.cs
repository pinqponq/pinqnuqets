using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Pinqponq.OpenApi;

internal sealed class ServersFromRequestTransformer(
    IHttpContextAccessor httpContextAccessor,
    IOptions<PinqponqOpenApiOptions> openApiOptions) : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        if (!openApiOptions.Value.ServerUrlFromRequest)
        {
            return Task.CompletedTask;
        }

        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null)
        {
            return Task.CompletedTask;
        }

        var serverUrl = $"{request.Scheme}://{request.Host.Value}{request.PathBase.Value}".TrimEnd('/');
        document.Servers = [new OpenApiServer { Url = serverUrl }];
        return Task.CompletedTask;
    }
}
