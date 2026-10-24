using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class CustomizerTests
{
    [Fact]
    public async Task Customizer_changes_the_document_and_the_operations()
    {
        await using var host = await OpenApiHost.Start(
            configureServices: services => services.AddSingleton<IPinqponqOpenApiCustomizer, RecordingCustomizer>());

        using var document = await host.GetDocument();

        Assert.Equal("Set by the customizer.", document.RootElement.GetProperty("info").GetProperty("summary").GetString());
        Assert.Equal("Operation customized.", document.Operation(OpenApiHost.OpenPath).GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Customizer_runs_after_the_package_has_added_the_security_schemes()
    {
        await using var host = await OpenApiHost.Start(
            configureServices: services => services.AddSingleton<IPinqponqOpenApiCustomizer, RecordingCustomizer>());

        using var document = await host.GetDocument();

        Assert.True(document.Operation(OpenApiHost.SecurePath).TryGetProperty("security", out _));
        Assert.Equal(
            "Bearer seen",
            document.RootElement.GetProperty("info").GetProperty("description").GetString());
    }

    [Fact]
    public async Task Customizers_run_in_registration_order()
    {
        await using var host = await OpenApiHost.Start(configureServices: services =>
        {
            services.AddSingleton<IPinqponqOpenApiCustomizer>(new TitleSuffixCustomizer("-first"));
            services.AddSingleton<IPinqponqOpenApiCustomizer>(new TitleSuffixCustomizer("-second"));
        });

        using var document = await host.GetDocument();

        Assert.EndsWith("-first-second", document.RootElement.GetProperty("info").GetProperty("title").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Customizer_that_implements_one_method_leaves_the_other_alone()
    {
        await using var host = await OpenApiHost.Start(
            configureServices: services => services.AddSingleton<IPinqponqOpenApiCustomizer, DocumentOnlyCustomizer>());

        using var document = await host.GetDocument();

        Assert.Equal("Document only.", document.RootElement.GetProperty("info").GetProperty("summary").GetString());
        Assert.False(document.Operation(OpenApiHost.OpenPath).TryGetProperty("summary", out _));
    }

    private sealed class RecordingCustomizer : IPinqponqOpenApiCustomizer
    {
        public Task CustomizeDocumentAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            document.Info.Summary = "Set by the customizer.";
            document.Info.Description = document.Components?.SecuritySchemes?.ContainsKey("Bearer") == true ? "Bearer seen" : "Bearer missing";
            return Task.CompletedTask;
        }

        public Task CustomizeOperationAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            operation.Summary = "Operation customized.";
            return Task.CompletedTask;
        }
    }

    private sealed class TitleSuffixCustomizer(string suffix) : IPinqponqOpenApiCustomizer
    {
        public Task CustomizeDocumentAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            document.Info.Title += suffix;
            return Task.CompletedTask;
        }
    }

    private sealed class DocumentOnlyCustomizer : IPinqponqOpenApiCustomizer
    {
        public Task CustomizeDocumentAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            document.Info.Summary = "Document only.";
            return Task.CompletedTask;
        }
    }
}
