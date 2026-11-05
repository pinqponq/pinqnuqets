using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Pinqponq.Auth.ApiKey.DependencyInjection;

/// <summary>
/// Registration helpers for an <c>HttpClient</c> that calls another service's endpoints guarded by
/// <see cref="RequireApiKeyAttribute"/>: the sending side of the key check.
/// </summary>
public static class InternalClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers a typed client whose base address, timeout and key header come from the
    /// <paramref name="sectionName"/> section (see <see cref="InternalClientOptions"/>). The section is
    /// validated on startup. Register several clients with the same section name to share one section.
    /// </summary>
    public static IHttpClientBuilder AddInternalApiKeyClient<TClient, TImplementation>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TClient : class
        where TImplementation : class, TClient
    {
        AddOptionsFor(services, configuration, sectionName);
        return services.AddHttpClient<TClient, TImplementation>(
            (provider, client) => Configure(provider, client, sectionName));
    }

    /// <summary>
    /// Registers a named client (<c>IHttpClientFactory.CreateClient(name)</c>) configured like
    /// <see cref="AddInternalApiKeyClient{TClient, TImplementation}"/>.
    /// </summary>
    public static IHttpClientBuilder AddInternalApiKeyClient(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        string sectionName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        AddOptionsFor(services, configuration, sectionName);
        return services.AddHttpClient(
            name,
            (provider, client) => Configure(provider, client, sectionName));
    }

    private static void AddOptionsFor(IServiceCollection services, IConfiguration configuration, string sectionName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<InternalClientOptions>, InternalClientOptionsValidator>());
        services.AddOptions<InternalClientOptions>(sectionName)
            .Bind(configuration.GetSection(sectionName))
            .ValidateOnStart();
    }

    private static void Configure(IServiceProvider provider, HttpClient client, string sectionName)
    {
        var options = provider.GetRequiredService<IOptionsMonitor<InternalClientOptions>>().Get(sectionName);

        client.BaseAddress = new Uri(options.BaseUrl.EndsWith('/') ? options.BaseUrl : options.BaseUrl + "/");
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client.DefaultRequestHeaders.Add(options.HeaderName, options.ApiKey);
    }
}
