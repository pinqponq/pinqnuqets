using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.OpenApi.DependencyInjection;

/// <summary>Registration helpers for Pinqponq OpenAPI.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the OpenAPI document <see cref="PinqponqOpenApiDefaults.DocumentName"/> with the settings in
    /// <paramref name="configure"/> and validates them on startup. Call <c>app.MapPinqponqOpenApi()</c> to serve it.
    /// </summary>
    public static IServiceCollection AddPinqponqOpenApi(
        this IServiceCollection services,
        Action<PinqponqOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<PinqponqOpenApiOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        return AddOpenApiServices(services);
    }

    /// <summary>
    /// Registers the OpenAPI document <see cref="PinqponqOpenApiDefaults.DocumentName"/> with the settings bound
    /// from <paramref name="configurationSection"/> and validates them on startup. <paramref name="configure"/>
    /// runs after binding. Call <c>app.MapPinqponqOpenApi()</c> to serve it.
    /// </summary>
    public static IServiceCollection AddPinqponqOpenApi(
        this IServiceCollection services,
        IConfiguration configurationSection,
        Action<PinqponqOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configurationSection);

        var optionsBuilder = services.AddOptions<PinqponqOpenApiOptions>().Bind(configurationSection);
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        return AddOpenApiServices(services);
    }

    private static IServiceCollection AddOpenApiServices(IServiceCollection services)
    {
        services.AddOptions<PinqponqOpenApiOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PinqponqOpenApiOptions>, PinqponqOpenApiOptionsValidator>());
        services.AddHttpContextAccessor();

        services.AddOpenApi(PinqponqOpenApiDefaults.DocumentName, openApiOptions =>
        {
            openApiOptions.AddDocumentTransformer<DocumentInfoTransformer>();
            openApiOptions.AddDocumentTransformer<SecuritySchemeTransformer>();
            openApiOptions.AddOperationTransformer<SecuritySchemeTransformer>();
            openApiOptions.AddDocumentTransformer<ServersFromRequestTransformer>();
            openApiOptions.AddDocumentTransformer<CustomizerTransformer>();
            openApiOptions.AddOperationTransformer<CustomizerTransformer>();
        });

        services.AddOptions<OpenApiOptions>(PinqponqOpenApiDefaults.DocumentName)
            .Configure<IOptions<PinqponqOpenApiOptions>>((openApiOptions, pinqponqOptions) =>
            {
                if (pinqponqOptions.Value.SchemaIdNaming == SchemaIdNaming.FullName)
                {
                    openApiOptions.CreateSchemaReferenceId = SchemaIdFactory.CreateFullNameId;
                }
            });

        return services;
    }
}
