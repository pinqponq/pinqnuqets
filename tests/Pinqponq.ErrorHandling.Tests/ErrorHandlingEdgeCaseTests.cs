using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling.DependencyInjection;
using Xunit;

namespace Pinqponq.ErrorHandling.Tests;

public sealed class ErrorHandlingEdgeCaseTests : IDisposable
{
    private readonly string _catalogDirectory = Path.Combine(Path.GetTempPath(), $"pinqponq-catalog-{Guid.NewGuid():N}");

    [Fact]
    public void Registration_helpers_reject_missing_arguments()
    {
        IServiceCollection missingServices = null!;

        Assert.Throws<ArgumentNullException>(() => missingServices.AddPinqponqErrorHandling());
        Assert.Throws<ArgumentNullException>(() => missingServices.AddPinqponqErrorLocalization());
        Assert.Throws<ArgumentNullException>(() => missingServices.AddPinqponqMaintenanceMode(new ConfigurationBuilder().Build()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddPinqponqMaintenanceMode(null!));
        Assert.Throws<ArgumentNullException>(() => missingServices.AddPinqponqExceptionResolver<NullExceptionResolver>());
        Assert.Throws<ArgumentNullException>(() => ((IApplicationBuilder)null!).UsePinqponqErrorHandling());
        Assert.Throws<ArgumentNullException>(() => ((IApplicationBuilder)null!).UsePinqponqMaintenanceMode());
        Assert.Throws<ArgumentNullException>(() => ErrorHandlingEvents.BuildExceptionEventName(null!));
    }

    [Fact]
    public void Registration_without_configuration_still_provides_options()
    {
        var services = new ServiceCollection();
        services.AddPinqponqErrorHandling();
        using var serviceProvider = services.BuildServiceProvider();

        Assert.NotNull(serviceProvider.GetService<IOptions<ErrorHandlingOptions>>());
    }

    [Fact]
    public void Localization_defaults_match_the_documented_values()
    {
        var localizationOptions = new ErrorLocalizationOptions();

        Assert.False(localizationOptions.Enabled);
        Assert.Equal("Language", localizationOptions.LanguageHeader);
        Assert.Equal("EN", localizationOptions.DefaultLanguage);
        Assert.Equal(CommonErrorCodes.GeneralError, localizationOptions.FallbackResponseCode);
        Assert.Equal("An unexpected error occurred.", localizationOptions.DefaultMessage);
        Assert.Equal("Localization", localizationOptions.CatalogDirectory);
    }

    [Fact]
    public async Task Each_outcome_is_logged_with_its_own_message()
    {
        var cancelledHarness = new ErrorResponseHarness();
        cancelledHarness.Options.TreatAnyCancellationAsClientClosed = true;
        var maintenanceHarness = new ErrorResponseHarness();
        maintenanceHarness.MaintenanceMode.Enabled = true;
        var failedHarness = new ErrorResponseHarness();

        await cancelledHarness.InvokeAsync(_ => throw new OperationCanceledException());
        await maintenanceHarness.InvokeAsync(_ => Task.CompletedTask);
        await failedHarness.InvokeAsync(_ => throw new KeyNotFoundException());

        Assert.StartsWith("Request cancelled by the client.", Assert.Single(cancelledHarness.Logger.Records).Message);
        Assert.StartsWith("Request blocked by maintenance mode.", Assert.Single(maintenanceHarness.MaintenanceLogger.Records).Message);
        Assert.StartsWith("Request failed. not_found 404", Assert.Single(failedHarness.Logger.Records).Message);
    }

    [Fact]
    public async Task Error_response_replaces_headers_and_output_of_the_failed_request()
    {
        var harness = new ErrorResponseHarness();

        var httpContext = await harness.InvokeAsync(context =>
        {
            context.Response.Headers["X-Partial"] = "1";
            throw new KeyNotFoundException();
        });

        Assert.False(httpContext.Response.Headers.ContainsKey("X-Partial"));
        Assert.Equal("application/json", httpContext.Response.ContentType);
    }

    [Fact]
    public async Task Failure_to_write_the_error_response_is_logged()
    {
        var harness = new ErrorResponseHarness();

        await harness.InvokeAsync(
            _ => throw new KeyNotFoundException(),
            context => context.Response.Body = new UnwritableStream());

        var writeFailure = Assert.Single(harness.Logger.Records, record => record.Level == LogLevel.Error);
        Assert.Equal("Failed to write the error response.", writeFailure.Message);
        Assert.IsType<IOException>(writeFailure.Exception);
    }

    [Fact]
    public async Task Throwing_status_mapping_resolver_is_logged_and_the_built_in_mapping_answers()
    {
        var harness = new ErrorResponseHarness();
        harness.Options.StatusMappingResolver = _ => throw new InvalidOperationException("resolver bug");

        var httpContext = await harness.InvokeAsync(_ => throw new KeyNotFoundException());

        Assert.Equal(StatusCodes.Status404NotFound, httpContext.Response.StatusCode);
        var resolverError = Assert.Single(harness.Logger.Records, record => record.Level == LogLevel.Error);
        Assert.Equal("StatusMappingResolver threw; falling back to default mapping.", resolverError.Message);
    }

    [Fact]
    public async Task Blank_language_and_correlation_headers_fall_back_to_their_defaults()
    {
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.CatalogDirectory = null;

        var httpContext = await harness.InvokeAsync(
            _ => throw new InvalidOperationException(CommonErrorCodes.ApiBodyRequired),
            context =>
            {
                context.TraceIdentifier = "trace-7";
                context.Request.Headers["Language"] = "  ";
                context.Request.Headers["X-Correlation-ID"] = " ";
            });

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("Request body is required.", body.RootElement.GetProperty("message").GetString());
        Assert.Equal("trace-7", body.RootElement.GetProperty("traceId").GetString());
        Assert.Equal("EN", httpContext.Features.Get<IRequestErrorFeature>()!.Detail["language"]);
    }

    [Theory]
    [InlineData("Pinqponq.ErrorHandling.Localization.EN.json", "EN")]
    [InlineData("App.Localization.tr.json", "TR")]
    [InlineData(".Localization.de.JSON", "DE")]
    [InlineData("App.Localization..json", null)]
    [InlineData("App.Localization.EN.txt", null)]
    [InlineData("App.readme.json", null)]
    [InlineData("Localization.EN.json", null)]
    public void Embedded_catalog_language_is_read_from_the_resource_name(string resourceName, string? expectedLanguage)
    {
        Assert.Equal(expectedLanguage, ErrorMessageCatalog.FindEmbeddedCatalogLanguage(resourceName));
    }

    [Fact]
    public async Task Embedded_catalogs_of_other_assemblies_are_read_and_other_resources_ignored()
    {
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
        harness.LocalizationOptions.CatalogDirectory = null;
        harness.LocalizationOptions.CatalogAssemblies.Add(typeof(ErrorHandlingEdgeCaseTests).Assembly);

        var httpContext = await harness.InvokeAsync(_ => throw new KeyNotFoundException("fixture_code"));

        using var body = ErrorResponseHarness.ReadBody(httpContext);
        Assert.Equal("fixture_code", body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Fixture message.", body.RootElement.GetProperty("message").GetString());
        Assert.Empty(harness.LoggerFactory.Records);
    }

    [Fact]
    public async Task Catalog_file_with_a_null_document_adds_nothing()
    {
        Directory.CreateDirectory(_catalogDirectory);
        await File.WriteAllTextAsync(Path.Combine(_catalogDirectory, "EN.json"), "null");
        var harness = new ErrorResponseHarness();
        harness.LocalizationOptions.Enabled = true;
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

    private sealed class NullExceptionResolver : IExceptionResolver
    {
        public ExceptionResolution? Resolve(Exception exception) => null;
    }

    private sealed class UnwritableStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("disk full");

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new IOException("disk full");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new IOException("disk full");
    }
}
