using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.Auth.ApiKey.DependencyInjection;
using Xunit;

namespace Pinqponq.Auth.ApiKey.Tests;

public sealed class InternalClientRegistrationTests
{
    private const string Section = "UsersClient";

    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>($"{Section}:{v.Key}", v.Value)))
            .Build();

    private static (string Key, string? Value)[] Valid() =>
    [
        ("BaseUrl", "https://users.internal"),
        ("ApiKey", "s3cret"),
        ("HeaderName", "X-Internal-Key"),
    ];

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddInternalApiKeyClient<IUsersClient, UsersClient>(configuration, Section);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Sets_base_address_with_trailing_slash_timeout_and_key_header()
    {
        using var provider = Build(Config([.. Valid(), ("TimeoutSeconds", "12")]));

        var http = ((UsersClient)provider.GetRequiredService<IUsersClient>()).Http;

        Assert.Equal(new Uri("https://users.internal/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(12), http.Timeout);
        Assert.Equal("s3cret", Assert.Single(http.DefaultRequestHeaders.GetValues("X-Internal-Key")));
    }

    [Fact]
    public void Keeps_existing_trailing_slash_and_path()
    {
        using var provider = Build(Config(
            ("BaseUrl", "https://users.internal/api/"), ("ApiKey", "k"), ("HeaderName", "X-K")));

        var http = ((UsersClient)provider.GetRequiredService<IUsersClient>()).Http;

        Assert.Equal(new Uri("https://users.internal/api/"), http.BaseAddress);
    }

    [Fact]
    public void Adds_trailing_slash_after_a_path_so_relative_requests_keep_it()
    {
        using var provider = Build(Config(
            ("BaseUrl", "https://users.internal/api"), ("ApiKey", "k"), ("HeaderName", "X-K")));

        var http = ((UsersClient)provider.GetRequiredService<IUsersClient>()).Http;

        Assert.Equal(new Uri("https://users.internal/api/"), http.BaseAddress);
        Assert.Equal(new Uri("https://users.internal/api/ping"), new Uri(http.BaseAddress!, "ping"));
    }

    [Fact]
    public void Timeout_defaults_to_five_seconds()
    {
        using var provider = Build(Config(Valid()));

        var http = ((UsersClient)provider.GetRequiredService<IUsersClient>()).Http;

        Assert.Equal(TimeSpan.FromSeconds(5), http.Timeout);
    }

    [Fact]
    public void Two_clients_share_one_section()
    {
        var services = new ServiceCollection();
        var configuration = Config(Valid());
        services.AddInternalApiKeyClient<IUsersClient, UsersClient>(configuration, Section);
        services.AddInternalApiKeyClient<IOrdersClient, OrdersClient>(configuration, Section);
        using var provider = services.BuildServiceProvider();

        var users = ((UsersClient)provider.GetRequiredService<IUsersClient>()).Http;
        var orders = ((OrdersClient)provider.GetRequiredService<IOrdersClient>()).Http;

        Assert.Equal(new Uri("https://users.internal/"), orders.BaseAddress);
        Assert.Equal("s3cret", Assert.Single(orders.DefaultRequestHeaders.GetValues("X-Internal-Key")));
        Assert.Equal(users.BaseAddress, orders.BaseAddress);
    }

    [Fact]
    public void Two_sections_keep_their_own_settings()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["A:BaseUrl"] = "https://a.internal",
            ["A:ApiKey"] = "key-a",
            ["A:HeaderName"] = "X-A",
            ["B:BaseUrl"] = "https://b.internal",
            ["B:ApiKey"] = "key-b",
            ["B:HeaderName"] = "X-B",
        }).Build();
        var services = new ServiceCollection();
        services.AddInternalApiKeyClient<IUsersClient, UsersClient>(configuration, "A");
        services.AddInternalApiKeyClient<IOrdersClient, OrdersClient>(configuration, "B");
        using var provider = services.BuildServiceProvider();

        var a = ((UsersClient)provider.GetRequiredService<IUsersClient>()).Http;
        var b = ((OrdersClient)provider.GetRequiredService<IOrdersClient>()).Http;

        Assert.Equal(new Uri("https://a.internal/"), a.BaseAddress);
        Assert.Equal("key-a", Assert.Single(a.DefaultRequestHeaders.GetValues("X-A")));
        Assert.Equal(new Uri("https://b.internal/"), b.BaseAddress);
        Assert.Equal("key-b", Assert.Single(b.DefaultRequestHeaders.GetValues("X-B")));
    }

    [Fact]
    public void Named_client_gets_the_same_settings()
    {
        var services = new ServiceCollection();
        services.AddInternalApiKeyClient("users", Config(Valid()), Section);
        using var provider = services.BuildServiceProvider();

        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient("users");

        Assert.Equal(new Uri("https://users.internal/"), http.BaseAddress);
        Assert.Equal("s3cret", Assert.Single(http.DefaultRequestHeaders.GetValues("X-Internal-Key")));
    }

    [Fact]
    public async Task Returns_the_builder_so_a_product_can_add_handlers()
    {
        var services = new ServiceCollection();
        var recorder = new RecordingHandler();

        services.AddInternalApiKeyClient<IUsersClient, UsersClient>(Config(Valid()), Section)
            .ConfigurePrimaryHttpMessageHandler(() => recorder);
        using var provider = services.BuildServiceProvider();
        var http = ((UsersClient)provider.GetRequiredService<IUsersClient>()).Http;

        await http.GetAsync("ping");

        Assert.Equal(new Uri("https://users.internal/ping"), recorder.LastRequest!.RequestUri);
        Assert.Equal("s3cret", Assert.Single(recorder.LastRequest.Headers.GetValues("X-Internal-Key")));
    }

    [Fact]
    public void Missing_section_fails_and_names_section_and_base_url()
    {
        using var provider = Build(new ConfigurationBuilder().Build());

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IUsersClient>());

        Assert.Contains($"BaseUrl must be an absolute http or https URL in section '{Section}'.", exception.Message);
    }

    [Theory]
    [InlineData("BaseUrl", "")]
    [InlineData("BaseUrl", "users.internal")]
    [InlineData("BaseUrl", "/relative/path")]
    [InlineData("BaseUrl", "ftp://users.internal")]
    public void Bad_base_url_fails_with_property_and_section(string property, string value)
    {
        using var provider = Build(Config(Replace(property, value)));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IUsersClient>());

        Assert.Contains($"{property} must be an absolute http or https URL in section '{Section}'.", exception.Message);
    }

    [Theory]
    [InlineData("ApiKey", "")]
    [InlineData("ApiKey", "  ")]
    [InlineData("HeaderName", "")]
    [InlineData("HeaderName", "  ")]
    public void Empty_key_or_header_name_fails_with_property_and_section(string property, string value)
    {
        using var provider = Build(Config(Replace(property, value)));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IUsersClient>());

        Assert.Contains($"{property} is required in section '{Section}'.", exception.Message);
    }

    [Fact]
    public void Header_name_that_is_not_a_header_fails()
    {
        using var provider = Build(Config(Replace("HeaderName", "Bad Header")));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IUsersClient>());

        Assert.Contains($"HeaderName is not a valid HTTP header name in section '{Section}'.", exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    public void Non_positive_timeout_fails_with_property_and_section(string value)
    {
        using var provider = Build(Config([.. Valid(), ("TimeoutSeconds", value)]));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IUsersClient>());

        Assert.Contains($"TimeoutSeconds must be positive in section '{Section}'.", exception.Message);
    }

    [Fact]
    public void Every_problem_is_reported_together()
    {
        using var provider = Build(new ConfigurationBuilder().Build());

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IUsersClient>());

        Assert.Equal(3, exception.Failures.Count());
    }

    [Fact]
    public void Invalid_section_stops_host_startup()
    {
        var services = new ServiceCollection();
        services.AddInternalApiKeyClient<IUsersClient, UsersClient>(Config(("BaseUrl", "https://users.internal")), Section);
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IStartupValidator>().Validate);
    }

    [Fact]
    public void Registration_arguments_are_checked()
    {
        var services = new ServiceCollection();
        var configuration = Config(Valid());

        Assert.Throws<ArgumentNullException>(
            () => InternalClientServiceCollectionExtensions.AddInternalApiKeyClient<IUsersClient, UsersClient>(null!, configuration, Section));
        Assert.Throws<ArgumentNullException>(
            () => services.AddInternalApiKeyClient<IUsersClient, UsersClient>(null!, Section));
        Assert.Throws<ArgumentException>(
            () => services.AddInternalApiKeyClient<IUsersClient, UsersClient>(configuration, " "));
        Assert.Throws<ArgumentException>(
            () => services.AddInternalApiKeyClient(" ", configuration, Section));
    }

    [Fact]
    public void Key_is_not_part_of_the_failure_message()
    {
        using var provider = Build(Config(Replace("BaseUrl", "")));

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IUsersClient>());

        Assert.DoesNotContain("s3cret", exception.Message);
    }

    private static (string Key, string? Value)[] Replace(string property, string value) =>
        [.. Valid().Where(v => v.Key != property), (property, value)];

    private interface IUsersClient;

    private interface IOrdersClient;

    private sealed class UsersClient(HttpClient http) : IUsersClient
    {
        public HttpClient Http { get; } = http;
    }

    private sealed class OrdersClient(HttpClient http) : IOrdersClient
    {
        public HttpClient Http { get; } = http;
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage());
        }
    }
}
