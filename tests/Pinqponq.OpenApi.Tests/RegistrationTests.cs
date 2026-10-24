using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pinqponq.OpenApi.DependencyInjection;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void Options_are_bound_from_a_configuration_section_and_configure_runs_after_binding()
    {
        var section = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenApi:Title"] = "From configuration",
                ["OpenApi:Description"] = "Bound description",
                ["OpenApi:ExposeIn"] = "Always",
                ["OpenApi:ApiKeyAuthentication:Enabled"] = "true",
                ["OpenApi:ApiKeyAuthentication:HeaderName"] = "X-Api-Key",
                ["OpenApi:BearerAuthentication:BearerFormat"] = "opaque",
            })
            .Build()
            .GetSection(PinqponqOpenApiOptions.SectionName);
        var services = new ServiceCollection();
        services.AddPinqponqOpenApi(section, options => options.Description = "From code");

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<PinqponqOpenApiOptions>>().Value;

        Assert.Equal("From configuration", options.Title);
        Assert.Equal("From code", options.Description);
        Assert.Equal(PinqponqOpenApiExposure.Always, options.ExposeIn);
        Assert.True(options.ApiKeyAuthentication.Enabled);
        Assert.Equal("X-Api-Key", options.ApiKeyAuthentication.HeaderName);
        Assert.Equal("opaque", options.BearerAuthentication.BearerFormat);
    }

    [Fact]
    public void Defaults_are_safe_and_neutral()
    {
        var options = new PinqponqOpenApiOptions();

        Assert.Equal(PinqponqOpenApiExposure.DevelopmentOnly, options.ExposeIn);
        Assert.Null(options.Title);
        Assert.Null(options.AuthorizationPolicy);
        Assert.False(options.ApiKeyAuthentication.Enabled);
        Assert.Null(options.ApiKeyAuthentication.HeaderName);
        Assert.True(options.BearerAuthentication.Enabled);
        Assert.True(options.ServerUrlFromRequest);
        Assert.Equal(SchemaIdNaming.Default, options.SchemaIdNaming);
        Assert.Equal(SecurityRequirementScope.AuthorizedOperations, options.SecurityRequirementScope);
        Assert.Equal("/openapi/{documentName}.json", options.OpenApiRoutePattern);
        Assert.Equal("/scalar", options.ScalarRoutePrefix);
    }

    [Fact]
    public async Task Invalid_options_fail_the_host_on_startup()
    {
        var start = () => OpenApiHost.Start(options => options.ApiKeyAuthentication.Enabled = true);

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(start);

        Assert.Contains("HeaderName", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_options_are_reported_when_the_host_starts_even_if_nothing_reads_them()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services => services.AddPinqponqOpenApi(options => options.ScalarRoutePrefix = "none"))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void Registering_without_a_service_collection_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddPinqponqOpenApi(null!));
    }

    [Fact]
    public void Registering_with_a_missing_configuration_section_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddPinqponqOpenApi((IConfiguration)null!));
    }

    [Fact]
    public void Mapping_on_a_missing_route_builder_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => EndpointRouteBuilderExtensions.MapPinqponqOpenApi(null!));
    }
}
