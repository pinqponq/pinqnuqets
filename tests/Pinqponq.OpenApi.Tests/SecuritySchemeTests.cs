using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class SecuritySchemeTests
{
    [Fact]
    public async Task Bearer_scheme_is_published_with_its_defaults_when_the_authentication_scheme_exists()
    {
        await using var host = await OpenApiHost.Start();

        using var document = await host.GetDocument();

        Assert.True(document.TryGetSecurityScheme("Bearer", out var scheme));
        Assert.Equal("http", scheme.GetProperty("type").GetString());
        Assert.Equal("bearer", scheme.GetProperty("scheme").GetString());
        Assert.Equal("JWT", scheme.GetProperty("bearerFormat").GetString());
        Assert.Equal("Bearer access token.", scheme.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Bearer_scheme_is_left_out_when_the_authentication_scheme_is_not_registered()
    {
        await using var host = await OpenApiHost.Start(registerBearerAuthentication: false);

        using var document = await host.GetDocument();

        Assert.False(document.TryGetSecurityScheme("Bearer", out _));
        Assert.False(document.TryGetSecurity(OpenApiHost.SecurePath, out _));
    }

    [Fact]
    public async Task Bearer_scheme_is_published_without_the_check_when_no_authentication_scheme_is_named()
    {
        await using var host = await OpenApiHost.Start(
            options => options.BearerAuthentication.AuthenticationScheme = null,
            registerBearerAuthentication: false);

        using var document = await host.GetDocument();

        Assert.True(document.TryGetSecurityScheme("Bearer", out _));
    }

    [Fact]
    public async Task Bearer_scheme_uses_the_configured_name_format_and_description()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.BearerAuthentication.SchemeName = "AccessToken";
            options.BearerAuthentication.BearerFormat = "opaque";
            options.BearerAuthentication.Description = "Token from the sign-in call.";
        });

        using var document = await host.GetDocument();

        Assert.False(document.TryGetSecurityScheme("Bearer", out _));
        Assert.True(document.TryGetSecurityScheme("AccessToken", out var scheme));
        Assert.Equal("opaque", scheme.GetProperty("bearerFormat").GetString());
        Assert.Equal("Token from the sign-in call.", scheme.GetProperty("description").GetString());
        Assert.True(document.TryGetSecurity(OpenApiHost.SecurePath, out var security));
        Assert.Equal(["AccessToken"], security.RequirementSchemeNames());
    }

    [Fact]
    public async Task Bearer_scheme_is_left_out_when_disabled()
    {
        await using var host = await OpenApiHost.Start(options => options.BearerAuthentication.Enabled = false);

        using var document = await host.GetDocument();

        Assert.False(document.TryGetSecurityScheme("Bearer", out _));
        Assert.False(document.TryGetSecurity(OpenApiHost.SecurePath, out _));
    }

    [Fact]
    public async Task Api_key_scheme_is_off_by_default()
    {
        await using var host = await OpenApiHost.Start();

        using var document = await host.GetDocument();

        Assert.False(document.TryGetSecurityScheme("ApiKey", out _));
    }

    [Fact]
    public async Task Api_key_scheme_is_published_in_the_configured_header()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.ApiKeyAuthentication.Enabled = true;
            options.ApiKeyAuthentication.HeaderName = "X-Custom-Key";
            options.ApiKeyAuthentication.Description = "Key for machine clients.";
        });

        using var document = await host.GetDocument();

        Assert.True(document.TryGetSecurityScheme("ApiKey", out var scheme));
        Assert.Equal("apiKey", scheme.GetProperty("type").GetString());
        Assert.Equal("header", scheme.GetProperty("in").GetString());
        Assert.Equal("X-Custom-Key", scheme.GetProperty("name").GetString());
        Assert.Equal("Key for machine clients.", scheme.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Requirement_is_added_only_to_authorized_operations()
    {
        await using var host = await OpenApiHost.Start();

        using var document = await host.GetDocument();

        Assert.True(document.TryGetSecurity(OpenApiHost.SecurePath, out var security));
        Assert.Equal(["Bearer"], security.RequirementSchemeNames());
        Assert.False(document.TryGetSecurity(OpenApiHost.OpenPath, out _));
    }

    [Fact]
    public async Task Requirement_is_added_to_an_operation_authorized_through_its_group()
    {
        await using var host = await OpenApiHost.Start();

        using var document = await host.GetDocument();

        Assert.True(document.TryGetSecurity(OpenApiHost.GuardedPath, out var security));
        Assert.Equal(["Bearer"], security.RequirementSchemeNames());
    }

    [Fact]
    public async Task Requirement_is_not_added_to_an_operation_that_allows_anonymous_callers()
    {
        await using var host = await OpenApiHost.Start();

        using var document = await host.GetDocument();

        Assert.False(document.TryGetSecurity(OpenApiHost.AllowAnonymousPath, out _));
    }

    [Fact]
    public async Task Requirement_is_added_to_every_operation_when_the_scope_is_all_operations()
    {
        await using var host = await OpenApiHost.Start(
            options => options.SecurityRequirementScope = SecurityRequirementScope.AllOperations);

        using var document = await host.GetDocument();

        foreach (var path in new[] { OpenApiHost.SecurePath, OpenApiHost.OpenPath, OpenApiHost.AllowAnonymousPath })
        {
            Assert.True(document.TryGetSecurity(path, out var security));
            Assert.Equal(["Bearer"], security.RequirementSchemeNames());
        }
    }

    [Fact]
    public async Task Two_credentials_are_two_alternative_requirements()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.ApiKeyAuthentication.Enabled = true;
            options.ApiKeyAuthentication.HeaderName = "X-Api-Key";
        });

        using var document = await host.GetDocument();

        Assert.True(document.TryGetSecurity(OpenApiHost.SecurePath, out var security));
        Assert.Equal(["Bearer", "ApiKey"], security.RequirementSchemeNames());
        Assert.False(document.TryGetSecurity(OpenApiHost.OpenPath, out _));
    }

    [Fact]
    public async Task Api_key_alone_is_published_when_the_bearer_authentication_scheme_is_missing()
    {
        await using var host = await OpenApiHost.Start(
            options =>
            {
                options.ApiKeyAuthentication.Enabled = true;
                options.ApiKeyAuthentication.HeaderName = "X-Api-Key";
            },
            registerBearerAuthentication: false);

        using var document = await host.GetDocument();

        Assert.True(document.TryGetSecurity(OpenApiHost.SecurePath, out var security));
        Assert.Equal(["ApiKey"], security.RequirementSchemeNames());
    }

    [Fact]
    public async Task Security_scheme_names_are_the_keys_of_the_requirements()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.ApiKeyAuthentication.Enabled = true;
            options.ApiKeyAuthentication.HeaderName = "X-Api-Key";
            options.ApiKeyAuthentication.SchemeName = "MachineKey";
        });

        using var document = await host.GetDocument();

        Assert.True(document.TryGetSecurityScheme("MachineKey", out _));
        Assert.True(document.TryGetSecurity(OpenApiHost.SecurePath, out var security));
        Assert.Equal(["Bearer", "MachineKey"], security.RequirementSchemeNames());
    }
}
