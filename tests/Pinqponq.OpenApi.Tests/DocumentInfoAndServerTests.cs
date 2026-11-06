using System.Text.Json;
using Xunit;

namespace Pinqponq.OpenApi.Tests;

public sealed class DocumentInfoAndServerTests
{
    private static async Task<string> GetServerUrl(OpenApiHost host, Action<HttpRequestMessage>? configureRequest = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, OpenApiHost.DefaultDocumentPath);
        configureRequest?.Invoke(request);
        using var response = await host.Client.SendAsync(request);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("servers")[0].GetProperty("url").GetString()!;
    }

    [Fact]
    public async Task Title_and_description_replace_the_info_of_the_document()
    {
        await using var host = await OpenApiHost.Start(options =>
        {
            options.Title = "Orders API";
            options.Description = "Orders for the storefront.";
        });

        using var document = await host.GetDocument();

        var info = document.RootElement.GetProperty("info");
        Assert.Equal("Orders API", info.GetProperty("title").GetString());
        Assert.Equal("Orders for the storefront.", info.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Info_keeps_the_ASP_NET_Core_title_when_none_is_set()
    {
        await using var host = await OpenApiHost.Start();

        using var document = await host.GetDocument();

        var info = document.RootElement.GetProperty("info");
        Assert.NotEqual(string.Empty, info.GetProperty("title").GetString());
        Assert.False(info.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task Server_url_follows_the_forwarded_scheme_and_host()
    {
        await using var host = await OpenApiHost.Start();

        var serverUrl = await GetServerUrl(host, request =>
        {
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-Host", "api.example.test");
        });

        Assert.Equal("https://api.example.test", serverUrl);
    }

    [Fact]
    public async Task Server_url_is_the_request_host_when_nothing_is_forwarded()
    {
        await using var host = await OpenApiHost.Start();

        var serverUrl = await GetServerUrl(host);

        Assert.Equal("http://localhost", serverUrl);
    }

    [Fact]
    public async Task Server_url_includes_the_path_base()
    {
        await using var host = await OpenApiHost.Start(pathBase: "/orders");

        using var response = await host.Client.GetAsync("/orders" + OpenApiHost.DefaultDocumentPath);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(
            "http://localhost/orders",
            document.RootElement.GetProperty("servers")[0].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Server_url_keeps_the_ASP_NET_Core_form_with_a_trailing_slash_when_taking_it_from_the_request_is_off()
    {
        await using var host = await OpenApiHost.Start(options => options.ServerUrlFromRequest = false);

        var serverUrl = await GetServerUrl(host, request =>
        {
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-Host", "api.example.test");
        });

        Assert.Equal("https://api.example.test/", serverUrl);
    }
}
