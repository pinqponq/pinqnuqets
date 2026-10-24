using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Pinqponq.OpenApi;

/// <summary>
/// Publishes the enabled security schemes in the document and adds a requirement to the operations that need
/// one. Each published scheme is a separate requirement, so a caller needs only one of them.
/// </summary>
internal sealed class SecuritySchemeTransformer(
    IOptions<PinqponqOpenApiOptions> openApiOptions,
    IAuthenticationSchemeProvider authenticationSchemeProvider) : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        var options = openApiOptions.Value;

        if (await IsBearerPublishedAsync(options.BearerAuthentication))
        {
            AddSecurityScheme(document, options.BearerAuthentication.SchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = options.BearerAuthentication.BearerFormat,
                Description = options.BearerAuthentication.Description,
            });
        }

        if (options.ApiKeyAuthentication.Enabled)
        {
            AddSecurityScheme(document, options.ApiKeyAuthentication.SchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Name = options.ApiKeyAuthentication.HeaderName,
                Description = options.ApiKeyAuthentication.Description,
            });
        }
    }

    public async Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var options = openApiOptions.Value;
        if (!NeedsSecurityRequirement(options.SecurityRequirementScope, context))
        {
            return;
        }

        var schemeNames = new List<string>();
        if (await IsBearerPublishedAsync(options.BearerAuthentication))
        {
            schemeNames.Add(options.BearerAuthentication.SchemeName);
        }

        if (options.ApiKeyAuthentication.Enabled)
        {
            schemeNames.Add(options.ApiKeyAuthentication.SchemeName);
        }

        if (schemeNames.Count == 0)
        {
            return;
        }

        operation.Security ??= [];
        foreach (var schemeName in schemeNames)
        {
            operation.Security.Add(new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(schemeName, context.Document)] = [],
            });
        }
    }

    private async Task<bool> IsBearerPublishedAsync(BearerAuthenticationOptions bearerOptions)
    {
        if (!bearerOptions.Enabled)
        {
            return false;
        }

        if (string.IsNullOrEmpty(bearerOptions.AuthenticationScheme))
        {
            return true;
        }

        return await authenticationSchemeProvider.GetSchemeAsync(bearerOptions.AuthenticationScheme) is not null;
    }

    private static bool NeedsSecurityRequirement(SecurityRequirementScope scope, OpenApiOperationTransformerContext context)
    {
        if (scope == SecurityRequirementScope.AllOperations)
        {
            return true;
        }

        var endpointMetadata = context.Description.ActionDescriptor.EndpointMetadata;
        if (endpointMetadata is null)
        {
            return false;
        }

        return endpointMetadata.OfType<IAuthorizeData>().Any()
            && !endpointMetadata.OfType<IAllowAnonymous>().Any();
    }

    private static void AddSecurityScheme(OpenApiDocument document, string schemeName, OpenApiSecurityScheme scheme)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[schemeName] = scheme;
    }
}
