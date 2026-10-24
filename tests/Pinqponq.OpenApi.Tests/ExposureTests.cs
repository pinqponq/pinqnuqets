using System.Net;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class ExposureTests
{
    [Theory]
    [InlineData("Development", HttpStatusCode.OK)]
    [InlineData("Production", HttpStatusCode.NotFound)]
    [InlineData("Test", HttpStatusCode.NotFound)]
    public async Task Default_exposure_serves_the_schema_and_the_reference_only_in_development(
        string environmentName,
        HttpStatusCode expectedStatus)
    {
        await using var host = await OpenApiHost.Start(environmentName: environmentName);

        using var documentResponse = await host.Client.GetAsync(OpenApiHost.DefaultDocumentPath);
        using var scalarResponse = await host.Client.GetAsync(OpenApiHost.DefaultScalarPath);

        Assert.Equal(expectedStatus, documentResponse.StatusCode);
        Assert.Equal(expectedStatus, scalarResponse.StatusCode);
    }

    [Fact]
    public async Task Always_serves_the_schema_and_the_reference_in_production()
    {
        await using var host = await OpenApiHost.Start(
            options => options.ExposeIn = PinqponqOpenApiExposure.Always,
            environmentName: "Production");

        using var documentResponse = await host.Client.GetAsync(OpenApiHost.DefaultDocumentPath);
        using var scalarResponse = await host.Client.GetAsync(OpenApiHost.DefaultScalarPath);

        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scalarResponse.StatusCode);
    }

    [Fact]
    public async Task Never_serves_nothing_even_in_development()
    {
        await using var host = await OpenApiHost.Start(options => options.ExposeIn = PinqponqOpenApiExposure.Never);

        using var documentResponse = await host.Client.GetAsync(OpenApiHost.DefaultDocumentPath);
        using var scalarResponse = await host.Client.GetAsync(OpenApiHost.DefaultScalarPath);

        Assert.Equal(HttpStatusCode.NotFound, documentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scalarResponse.StatusCode);
    }

    [Fact]
    public async Task Application_endpoints_stay_mapped_when_the_schema_is_not_served()
    {
        await using var host = await OpenApiHost.Start(environmentName: "Production");

        using var response = await host.Client.GetAsync(OpenApiHost.OpenPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Custom_routes_replace_the_default_ones()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.OpenApiRoutePattern = "/api/landing/openapi/{documentName}.json";
            options.ScalarRoutePrefix = "/api/landing/docs";
        });

        using var documentResponse = await host.Client.GetAsync("/api/landing/openapi/v1.json");
        using var scalarResponse = await host.Client.GetAsync("/api/landing/docs/v1");
        using var oldDocumentResponse = await host.Client.GetAsync(OpenApiHost.DefaultDocumentPath);
        using var oldScalarResponse = await host.Client.GetAsync(OpenApiHost.DefaultScalarPath);

        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, scalarResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldDocumentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oldScalarResponse.StatusCode);
    }

    [Fact]
    public async Task Scalar_page_loads_the_document_from_the_custom_route()
    {
        await using var host = await OpenApiHost.Start(options =>
            options.OpenApiRoutePattern = "/api/landing/openapi/{documentName}.json");

        var page = await host.Client.GetStringAsync(OpenApiHost.DefaultScalarPath);

        Assert.Contains("api/landing/openapi/v1.json", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Authorization_policy_keeps_anonymous_callers_out_of_the_schema_and_the_reference()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.ExposeIn = PinqponqOpenApiExposure.Always;
            options.AuthorizationPolicy = "docs";
        });

        using var documentResponse = await host.Client.GetAsync(OpenApiHost.DefaultDocumentPath);
        using var scalarResponse = await host.Client.GetAsync(OpenApiHost.DefaultScalarPath);

        Assert.Equal(HttpStatusCode.Unauthorized, documentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, scalarResponse.StatusCode);
    }

    [Fact]
    public async Task Authorization_policy_lets_a_caller_who_satisfies_it_in()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.ExposeIn = PinqponqOpenApiExposure.Always;
            options.AuthorizationPolicy = "docs";
        });
        using var request = new HttpRequestMessage(HttpMethod.Get, OpenApiHost.DefaultDocumentPath);
        request.Headers.Add(OpenApiHost.UserHeader, "reader");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Empty_authorization_policy_requires_an_authenticated_user()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.ExposeIn = PinqponqOpenApiExposure.Always;
            options.AuthorizationPolicy = string.Empty;
        });
        using var signedIn = new HttpRequestMessage(HttpMethod.Get, OpenApiHost.DefaultDocumentPath);
        signedIn.Headers.Add(OpenApiHost.UserHeader, "reader");

        using var anonymousResponse = await host.Client.GetAsync(OpenApiHost.DefaultDocumentPath);
        using var signedInResponse = await host.Client.SendAsync(signedIn);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, signedInResponse.StatusCode);
    }

    [Fact]
    public async Task Without_an_authorization_policy_anonymous_callers_can_read_the_schema()
    {
        await using var host = await OpenApiHost.Start(options => options.ExposeIn = PinqponqOpenApiExposure.Always);

        using var response = await host.Client.GetAsync(OpenApiHost.DefaultDocumentPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
