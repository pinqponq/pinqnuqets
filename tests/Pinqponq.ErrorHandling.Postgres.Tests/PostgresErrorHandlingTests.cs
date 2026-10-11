using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Pinqponq.Contracts;
using Pinqponq.ErrorHandling.DependencyInjection;
using Pinqponq.ErrorHandling.Postgres.DependencyInjection;
using Xunit;

namespace Pinqponq.ErrorHandling.Postgres.Tests;

public sealed class PostgresErrorHandlingTests
{
    private static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPinqponqErrorHandling();
        services.AddPinqponqErrorLocalization(localizationOptions => localizationOptions.CatalogDirectory = null);
        services.AddPinqponqPostgresErrorHandling();
        return services.BuildServiceProvider();
    }

    private static async Task<HttpContext> InvokeAsync(ServiceProvider serviceProvider, Exception exception, string language)
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw exception,
            serviceProvider.GetRequiredService<IOptions<ErrorHandlingOptions>>(),
            serviceProvider.GetRequiredService<IOptions<ErrorLocalizationOptions>>(),
            serviceProvider.GetServices<IExceptionResolver>(),
            NullLoggerFactory.Instance,
            NullLogger<ExceptionHandlingMiddleware>.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Language"] = language;
        httpContext.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(httpContext);

        httpContext.Response.Body.Position = 0;
        return httpContext;
    }

    [Fact]
    public async Task Unique_violation_is_answered_with_a_localized_database_message()
    {
        using var serviceProvider = BuildServiceProvider();

        var httpContext = await InvokeAsync(
            serviceProvider,
            PostgresExceptionResolverTests.CreatePostgresException(PostgresErrorCodes.UniqueViolation),
            "tr");

        using var body = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(StatusCodes.Status400BadRequest, httpContext.Response.StatusCode);
        Assert.Equal(PostgresErrorResponseCodes.DatabaseUniqueViolation, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("Bu kayıt zaten mevcut.", body.RootElement.GetProperty("message").GetString());
        var errorFeature = httpContext.Features.Get<IRequestErrorFeature>();
        Assert.NotNull(errorFeature);
        Assert.Equal("exception_PostgresException", errorFeature.EventName);
        Assert.Equal("23505", errorFeature.Detail["sqlState"]);
        Assert.Equal("TR", errorFeature.Detail["language"]);
    }

    [Fact]
    public async Task Unmapped_sql_state_is_answered_with_the_database_error_message()
    {
        using var serviceProvider = BuildServiceProvider();

        var httpContext = await InvokeAsync(
            serviceProvider,
            PostgresExceptionResolverTests.CreatePostgresException(PostgresErrorCodes.DeadlockDetected),
            "en");

        using var body = await JsonDocument.ParseAsync(httpContext.Response.Body);
        Assert.Equal(PostgresErrorResponseCodes.DatabaseError, body.RootElement.GetProperty("responseCode").GetString());
        Assert.Equal("A database error occurred.", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public void Registering_twice_adds_one_resolver_and_one_catalog()
    {
        var services = new ServiceCollection();
        services.AddPinqponqPostgresErrorHandling();
        services.AddPinqponqPostgresErrorHandling();
        using var serviceProvider = services.BuildServiceProvider();

        var localizationOptions = serviceProvider.GetRequiredService<IOptions<ErrorLocalizationOptions>>().Value;
        Assert.IsType<PostgresExceptionResolver>(Assert.Single(serviceProvider.GetServices<IExceptionResolver>()));
        Assert.Single(localizationOptions.CatalogAssemblies, assembly => assembly == typeof(PostgresExceptionResolver).Assembly);
        Assert.Contains(typeof(ErrorLocalizationOptions).Assembly, localizationOptions.CatalogAssemblies);
    }

    [Fact]
    public void Every_catalog_language_has_every_database_code()
    {
        var catalogAssembly = typeof(PostgresExceptionResolver).Assembly;
        var catalogResourceNames = catalogAssembly.GetManifestResourceNames()
            .Where(resourceName => resourceName.Contains(".Localization.", StringComparison.Ordinal))
            .ToList();
        string[] databaseCodes =
        [
            PostgresErrorResponseCodes.DatabaseError,
            PostgresErrorResponseCodes.DatabaseForeignKeyViolation,
            PostgresErrorResponseCodes.DatabaseUniqueViolation,
            PostgresErrorResponseCodes.DatabaseNotNullViolation,
            PostgresErrorResponseCodes.DatabaseValueTooLong,
            PostgresErrorResponseCodes.DatabaseInvalidFormat,
        ];

        Assert.Equal(12, catalogResourceNames.Count);
        foreach (var catalogResourceName in catalogResourceNames)
        {
            using var catalogStream = catalogAssembly.GetManifestResourceStream(catalogResourceName)!;
            var messages = JsonSerializer.Deserialize<Dictionary<string, string>>(catalogStream)!;
            Assert.Equal(databaseCodes.Order(), messages.Keys.Order());
        }
    }
}
