using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Pinqponq.Contracts;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class LocalizedErrorResponseTests : IDisposable
{
    private readonly string _catalogDirectory = Path.Combine(Path.GetTempPath(), $"pinqponq-catalog-{Guid.NewGuid():N}");

    private static ErrorResponseHarness CreateLocalizedHarness()
    {
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.ExceptionResolvers.Add(new MessageAsResponseCodeResolver());
        harness.LocalizationOptions.CatalogDirectory = null;
        return harness;
    }

    [Fact]
    public async Task Exception_message_known_to_the_catalog_is_the_response_code_with_the_message_in_the_requested_language()
    {
        var harness = CreateLocalizedHarness();

        var httpContext = await harness.InvokeAsync(
            _ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired),
            context => context.Request.Headers["Accept-Language"] = "tr");

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal(CommonErrorCodes.ApiBodyRequired, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("İstek gövdesi gerekli.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Exception_message_unknown_to_the_catalog_falls_back_to_general_error_in_english()
    {
        var harness = CreateLocalizedHarness();

        var httpContext = await harness.InvokeAsync(_ => throw new Exception("connection refused by 10.0.0.4"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(StatusCodes.Status500InternalServerError, httpContext.Response.StatusCode);
        Assert.Equal(CommonErrorCodes.GeneralError, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("An unexpected error occurred.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Language_without_a_catalog_answers_in_the_default_language()
    {
        var harness = CreateLocalizedHarness();

        var httpContext = await harness.InvokeAsync(
            _ => throw new InvalidOperationException(CommonErrorCodes.ApiPageSizeInvalid),
            context => context.Request.Headers["Accept-Language"] = "XX");

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("Page size must be greater than 0.", body.RootElement.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("tr-TR,tr;q=0.9,en;q=0.8", "İstek gövdesi gerekli.")]
    [InlineData(" de-AT ; q=1", "Der Anfragetext ist erforderlich.")]
    [InlineData("xx-TR", "Request body is required.")]
    public async Task First_accept_language_tag_is_used_and_falls_back_to_its_primary_language(string acceptLanguage, string expectedMessage)
    {
        var harness = CreateLocalizedHarness();

        var httpContext = await harness.InvokeAsync(
            _ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired),
            context => context.Request.Headers["Accept-Language"] = acceptLanguage);

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(expectedMessage, body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Catalog_for_the_full_language_tag_wins_over_its_primary_language()
    {
        Directory.CreateDirectory(_catalogDirectory);
        await File.WriteAllTextAsync(Path.Combine(_catalogDirectory, "PT-BR.json"), """{ "api_body_required": "Corpo obrigatório (BR)." }""");
        var harness = CreateLocalizedHarness();
        harness.LocalizationOptions.CatalogDirectory = _catalogDirectory;

        var brazilian = await harness.InvokeAsync(
            _ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired),
            context => context.Request.Headers["Accept-Language"] = "pt-BR");
        var portuguese = await harness.InvokeAsync(
            _ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired),
            context => context.Request.Headers["Accept-Language"] = "pt-PT");

        using var brazilianBody = ErrorResponseHarness.ReadBody(brazilian);
        using var portugueseBody = ErrorResponseHarness.ReadBody(portuguese);
        Assert.Equal("Corpo obrigatório (BR).", brazilianBody.RootElement.GetProperty("message").GetString());
        Assert.Equal("O corpo da solicitação é obrigatório.", portugueseBody.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task By_default_the_mapped_response_code_is_looked_up_not_the_exception_message()
    {
        Directory.CreateDirectory(_catalogDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_catalogDirectory, "EN.json"),
            """{ "not_found": "Nothing here.", "order_not_found": "Order not found." }""");
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.CatalogDirectory = _catalogDirectory;

        var mapped = await harness.InvokeAsync(_ => throw new KeyNotFoundException("order_not_found"));
        var unknownMapped = await harness.InvokeAsync(_ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired));

        using var mappedBody = ErrorResponseHarness.ReadBody(mapped);
        Assert.Equal(StatusCodes.Status404NotFound, mapped.Response.StatusCode);
        Assert.Equal("not_found", mappedBody.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Nothing here.", mappedBody.RootElement.GetProperty("message").GetString());
        using var unknownMappedBody = ErrorResponseHarness.ReadBody(unknownMapped);
        Assert.Equal(StatusCodes.Status400BadRequest, unknownMapped.Response.StatusCode);
        Assert.Equal("bad_request", unknownMappedBody.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("An unexpected error occurred.", unknownMappedBody.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unknown_mapped_code_gets_the_fallback_message_in_the_requested_language()
    {
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.CatalogDirectory = null;

        var httpContext = await harness.InvokeAsync(
            _ => throw new KeyNotFoundException("missing"),
            context => context.Request.Headers["Accept-Language"] = "tr");

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("not_found", body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Beklenmeyen bir hata oluştu.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Default_message_is_used_when_neither_the_code_nor_the_fallback_has_a_message()
    {
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.CatalogDirectory = null;
        harness.LocalizationOptions.FallbackResponseCode = "order_error";
        harness.LocalizationOptions.DefaultMessage = "Something went wrong.";

        var httpContext = await harness.InvokeAsync(_ => throw new KeyNotFoundException("missing"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("not_found", body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Something went wrong.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Configured_fallback_code_and_default_message_are_used_when_the_catalog_has_neither()
    {
        var harness = CreateLocalizedHarness();
        harness.LocalizationOptions.FallbackResponseCode = "order_error";
        harness.LocalizationOptions.DefaultMessage = "Something went wrong.";

        var httpContext = await harness.InvokeAsync(_ => throw new Exception("unknown"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("order_error", body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Something went wrong.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Body_has_the_base_response_fields_in_order_then_the_trace_id()
    {
        var harness = CreateLocalizedHarness();

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        var fieldNames = body.RootElement.EnumerateObject().Select(field => field.Name).ToArray();
        Assert.Equal(["status", "statusCode", "message", "responseCode", "traceId"], fieldNames);
        Assert.False(body.RootElement.GetProperty("status").GetBoolean());
        Assert.Equal(400, body.RootElement.GetProperty("statusCode").GetInt32());
    }

    [Fact]
    public async Task Localized_mode_ignores_IncludeExceptionMessage()
    {
        var harness = CreateLocalizedHarness();
        harness.Options.IncludeExceptionMessage = true;

        var httpContext = await harness.InvokeAsync(_ => throw new Exception("secret detail"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("An unexpected error occurred.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Application_catalog_directory_adds_codes_and_overrides_embedded_messages()
    {
        Directory.CreateDirectory(_catalogDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(_catalogDirectory, "en.json"),
            """{ "order_not_found": "Order was not found.", "general_error": "Order service failed." }""");
        var harness = CreateLocalizedHarness();
        harness.LocalizationOptions.CatalogDirectory = _catalogDirectory;

        var knownCodeContext = await harness.InvokeAsync(_ => throw new KeyNotFoundException("order_not_found"));
        var unknownCodeContext = await harness.InvokeAsync(_ => throw new Exception("unknown"));

        using var knownCodeBody = ErrorResponseHarness.ReadBody(knownCodeContext);
        using var unknownCodeBody = ErrorResponseHarness.ReadBody(unknownCodeContext);
        Assert.Equal(StatusCodes.Status404NotFound, knownCodeContext.Response.StatusCode);
        Assert.Equal("order_not_found", knownCodeBody.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Order was not found.", knownCodeBody.RootElement.GetProperty("message").GetString());
        Assert.Equal("Order service failed.", unknownCodeBody.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Unreadable_catalog_file_is_logged_and_skipped()
    {
        Directory.CreateDirectory(_catalogDirectory);
        await File.WriteAllTextAsync(Path.Combine(_catalogDirectory, "EN.json"), "{ not json");
        await File.WriteAllTextAsync(Path.Combine(_catalogDirectory, "TR.json"), """{ "order_not_found": "Sipariş bulunamadı." }""");
        var harness = CreateLocalizedHarness();
        harness.LocalizationOptions.CatalogDirectory = _catalogDirectory;

        var httpContext = await harness.InvokeAsync(
            _ => throw new KeyNotFoundException("order_not_found"),
            context => context.Request.Headers["Accept-Language"] = "TR");

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("Sipariş bulunamadı.", body.RootElement.GetProperty("message").GetString());
        var catalogError = Assert.Single(harness.LoggerFactory.Records, record => record.Level == LogLevel.Error);
        Assert.Contains("EN.json", catalogError.Message);
        Assert.NotNull(catalogError.Exception);
    }

    [Fact]
    public async Task Missing_catalog_directory_keeps_the_embedded_catalog()
    {
        var harness = CreateLocalizedHarness();
        harness.LocalizationOptions.CatalogDirectory = _catalogDirectory;

        var httpContext = await harness.InvokeAsync(_ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal(CommonErrorCodes.ApiBodyRequired, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Empty(harness.LoggerFactory.Records);
    }

    public void Dispose()
    {
        if (Directory.Exists(_catalogDirectory))
        {
            Directory.Delete(_catalogDirectory, recursive: true);
        }
    }
}
