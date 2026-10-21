using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling.DependencyInjection;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class ErrorResponseWriterTests
{
    private const string DefaultMessage = "Too many requests. Try again later.";
    private const string EventName = "request_rate_limited";

    private static IErrorResponseWriter CreateWriter(Action<IServiceCollection>? configureServices = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqErrorHandling(options => options.CorrelationIdHeader = "X-Request-Id");
        configureServices?.Invoke(services);
        return services.BuildServiceProvider().GetRequiredService<IErrorResponseWriter>();
    }

    private static DefaultHttpContext CreateContext(string? language = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = HttpMethods.Post;
        httpContext.Response.Body = new MemoryStream();
        if (language is not null)
        {
            httpContext.Request.Headers["Accept-Language"] = language;
        }

        return httpContext;
    }

    private static void EnableLocalization(IServiceCollection services) =>
        services.AddPinqponqErrorLocalization(options => options.CatalogDirectory = null);

    [Fact]
    public async Task Writes_the_error_body_with_the_default_message_when_localization_is_off()
    {
        var writer = CreateWriter();
        var httpContext = CreateContext(language: "tr");
        httpContext.Request.Headers["X-Request-Id"] = "request-42";

        await writer.Write(httpContext, StatusCodes.Status429TooManyRequests, CommonErrorCodes.TooManyRequests, DefaultMessage, EventName);

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status429TooManyRequests, httpContext.Response.StatusCode);
        Assert.Equal("application/json", httpContext.Response.ContentType);
        Assert.False(body.RootElement.GetProperty("status").GetBoolean());
        Assert.Equal(StatusCodes.Status429TooManyRequests, body.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal(CommonErrorCodes.TooManyRequests, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal(DefaultMessage, body.RootElement.GetProperty("message").GetString());
        Assert.Equal("request-42", body.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task Writes_the_catalog_message_in_the_request_language_when_localization_is_on()
    {
        var writer = CreateWriter(EnableLocalization);
        var httpContext = CreateContext(language: "de-DE,de;q=0.9");

        await writer.Write(httpContext, StatusCodes.Status429TooManyRequests, CommonErrorCodes.TooManyRequests, DefaultMessage, EventName);

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(CommonErrorCodes.TooManyRequests, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Zu viele Anfragen. Bitte versuche es später erneut.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Uses_the_default_language_when_the_request_has_none()
    {
        var writer = CreateWriter(EnableLocalization);
        var httpContext = CreateContext();

        await writer.Write(httpContext, StatusCodes.Status429TooManyRequests, CommonErrorCodes.TooManyRequests, "unused", EventName);

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(DefaultMessage, body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Code_unknown_to_the_catalog_is_kept_and_gets_the_fallback_message()
    {
        var writer = CreateWriter(EnableLocalization);
        var httpContext = CreateContext(language: "tr");

        await writer.Write(httpContext, StatusCodes.Status409Conflict, "order_locked", "Order is locked.", EventName);

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("order_locked", body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Beklenmeyen bir hata oluştu.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Uses_the_options_default_message_when_the_catalog_has_no_message_at_all()
    {
        var writer = CreateWriter(services => services.AddPinqponqErrorLocalization(options =>
        {
            options.CatalogDirectory = null;
            options.CatalogAssemblies.Clear();
            options.DefaultMessage = "Something went wrong.";
        }));
        var httpContext = CreateContext();

        await writer.Write(httpContext, StatusCodes.Status429TooManyRequests, CommonErrorCodes.TooManyRequests, DefaultMessage, EventName);

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("Something went wrong.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Describes_the_failure_on_the_request_error_feature()
    {
        var writer = CreateWriter(EnableLocalization);
        var httpContext = CreateContext(language: "tr");

        await writer.Write(httpContext, StatusCodes.Status429TooManyRequests, CommonErrorCodes.TooManyRequests, DefaultMessage, EventName);

        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal(EventName, errorFeature.EventName);
        Assert.Equal(StatusCodes.Status429TooManyRequests, errorFeature.Detail["statusCode"]);
        Assert.Equal(CommonErrorCodes.TooManyRequests, errorFeature.Detail["errorCode"]);
        Assert.Equal("Çok fazla istek gönderildi. Daha sonra tekrar deneyin.", errorFeature.Detail["errorMessage"]);
        Assert.Equal("TR", errorFeature.Detail["language"]);
    }

    [Fact]
    public async Task Request_error_feature_has_no_language_when_localization_is_off()
    {
        var writer = CreateWriter();
        var httpContext = CreateContext(language: "tr");

        await writer.Write(httpContext, StatusCodes.Status429TooManyRequests, CommonErrorCodes.TooManyRequests, DefaultMessage, EventName);

        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.False(errorFeature.Detail.ContainsKey("language"));
    }

    [Fact]
    public async Task Rejects_missing_arguments()
    {
        var writer = CreateWriter();
        var httpContext = CreateContext();

        await Assert.ThrowsAsync<ArgumentNullException>(() => writer.Write(null!, 429, CommonErrorCodes.TooManyRequests, DefaultMessage, EventName));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.Write(httpContext, 429, " ", DefaultMessage, EventName));
        await Assert.ThrowsAsync<ArgumentNullException>(() => writer.Write(httpContext, 429, CommonErrorCodes.TooManyRequests, null!, EventName));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.Write(httpContext, 429, CommonErrorCodes.TooManyRequests, DefaultMessage, ""));
    }

    [Fact]
    public void Registering_error_handling_twice_keeps_one_writer()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqErrorHandling();
        services.AddPinqponqErrorHandling();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IErrorResponseWriter));
    }
}

public sealed class EmbeddedCatalogTests
{
    [Fact]
    public void Every_embedded_language_has_the_codes_of_the_english_catalog()
    {
        var catalogAssembly = typeof(ErrorLocalizationOptions).Assembly;
        var codesByResource = catalogAssembly.GetManifestResourceNames()
            .Where(resourceName => ErrorMessageCatalog.FindEmbeddedCatalogLanguage(resourceName) is not null)
            .ToDictionary(resourceName => resourceName, resourceName => ReadCodes(catalogAssembly, resourceName));
        var englishCodes = codesByResource.Single(entry => entry.Key.EndsWith(".EN.json", StringComparison.Ordinal)).Value;

        Assert.Contains(CommonErrorCodes.TooManyRequests, englishCodes);
        Assert.Equal(12, codesByResource.Count);
        Assert.All(codesByResource, entry => Assert.Equal(englishCodes, entry.Value));
    }

    private static SortedSet<string> ReadCodes(System.Reflection.Assembly catalogAssembly, string resourceName)
    {
        using var catalogStream = catalogAssembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Catalog '{resourceName}' could not be opened.");
        var messages = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(catalogStream) ?? [];
        return new SortedSet<string>(messages.Keys, StringComparer.Ordinal);
    }
}
