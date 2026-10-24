using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class TransformerUnitTests
{
    private static SecuritySchemeTransformer CreateSecurityTransformer(PinqponqOpenApiOptions options)
    {
        var authenticationOptions = new AuthenticationOptions();
        authenticationOptions.AddScheme("Bearer", scheme => scheme.HandlerType = typeof(OpenApiHost.TestAuthenticationHandler));
        var schemeProvider = new AuthenticationSchemeProvider(Options.Create(authenticationOptions));
        return new SecuritySchemeTransformer(Options.Create(options), schemeProvider);
    }

    private static OpenApiOperationTransformerContext CreateOperationContext(IList<object>? endpointMetadata)
    {
        return new OpenApiOperationTransformerContext
        {
            DocumentName = PinqponqOpenApiDefaults.DocumentName,
            Description = new ApiDescription { ActionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata! } },
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        };
    }

    [Fact]
    public async Task Operation_without_endpoint_metadata_gets_no_requirement()
    {
        var transformer = CreateSecurityTransformer(new PinqponqOpenApiOptions());
        var operation = new OpenApiOperation();

        await transformer.TransformAsync(operation, CreateOperationContext(null), CancellationToken.None);

        Assert.Null(operation.Security);
    }

    [Fact]
    public async Task Existing_requirements_of_an_operation_are_kept()
    {
        var transformer = CreateSecurityTransformer(new PinqponqOpenApiOptions());
        var existing = new OpenApiSecurityRequirement();
        var operation = new OpenApiOperation { Security = [existing] };
        var metadata = new List<object> { new Microsoft.AspNetCore.Authorization.AuthorizeAttribute() };

        await transformer.TransformAsync(operation, CreateOperationContext(metadata), CancellationToken.None);

        Assert.Equal(2, operation.Security.Count);
        Assert.Same(existing, operation.Security[0]);
    }

    [Fact]
    public async Task Server_url_is_not_changed_when_there_is_no_current_request()
    {
        var transformer = new ServersFromRequestTransformer(new HttpContextAccessor(), Options.Create(new PinqponqOpenApiOptions()));
        var document = new OpenApiDocument { Servers = [new OpenApiServer { Url = "http://original/" }] };

        await transformer.TransformAsync(document, new OpenApiDocumentTransformerContext
        {
            DocumentName = PinqponqOpenApiDefaults.DocumentName,
            DescriptionGroups = [],
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, CancellationToken.None);

        Assert.Equal("http://original/", Assert.Single(document.Servers).Url);
    }
}
