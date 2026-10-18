using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Pinqponq.Storage.S3.DependencyInjection;
using Xunit;

namespace Pinqponq.Storage.S3.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void Registers_storage_and_one_shared_client()
    {
        var services = new ServiceCollection();
        services.AddPinqponqObjectStorage(ConfigureAws);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<S3ObjectStorage>(provider.GetRequiredService<IObjectStorage>());
        Assert.IsType<AmazonS3Client>(provider.GetRequiredService<IAmazonS3>());
        Assert.Same(provider.GetRequiredService<IAmazonS3>(), provider.GetRequiredService<IAmazonS3>());
        Assert.Same(provider.GetRequiredService<IObjectStorage>(), provider.GetRequiredService<IObjectStorage>());
    }

    [Fact]
    public void Client_registered_by_the_application_wins()
    {
        IAmazonS3 client = new FakeStorageClient();
        var services = new ServiceCollection();
        services.AddSingleton(client);
        services.AddPinqponqObjectStorage(ConfigureAws);
        using var provider = services.BuildServiceProvider();

        Assert.Same(client, provider.GetRequiredService<IAmazonS3>());
    }

    [Fact]
    public void Binds_options_from_a_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:BucketName"] = "bucket-from-settings",
                ["Storage:AccessKey"] = "access",
                ["Storage:SecretKey"] = "secret",
                ["Storage:ServiceUrl"] = "https://minio.example.com",
                ["Storage:DisablePayloadSigning"] = "true",
                ["Storage:RequestChecksumMode"] = "WhenSupported"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddPinqponqObjectStorage(configuration.GetSection("Storage"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var storageOptions = provider.GetRequiredService<IOptions<ObjectStorageOptions>>().Value;

        Assert.Equal("bucket-from-settings", storageOptions.BucketName);
        Assert.Equal("https://minio.example.com", storageOptions.ServiceUrl);
        Assert.True(storageOptions.DisablePayloadSigning);
        Assert.Equal(ObjectStorageChecksumMode.WhenSupported, storageOptions.RequestChecksumMode);
    }

    [Fact]
    public void Missing_configuration_section_fails_at_registration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddPinqponqObjectStorage(configuration.GetSection("Storage")));

        Assert.Contains("'Storage'", exception.Message);
    }

    [Fact]
    public void Invalid_options_stop_startup()
    {
        var services = new ServiceCollection();
        services.AddPinqponqObjectStorage(storage => storage.Region = StorageOptionsFactory.REGION);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("BucketName is required.", exception.Message);
    }

    [Fact]
    public void Registering_twice_adds_the_validator_once()
    {
        var services = new ServiceCollection();
        services.AddPinqponqObjectStorage(ConfigureAws);
        services.AddPinqponqObjectStorage(ConfigureAws);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IValidateOptions<ObjectStorageOptions>));
    }

    [Fact]
    public void Health_check_is_registered_with_the_default_name()
    {
        var services = new ServiceCollection();
        services.AddPinqponqObjectStorage(ConfigureAws);
        services.AddHealthChecks().AddPinqponqObjectStorageHealthCheck(tags: ["ready"]);
        using var provider = services.BuildServiceProvider();

        var registration = Assert.Single(provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations);

        Assert.Equal("object-storage", registration.Name);
        Assert.Equal(HealthStatus.Unhealthy, registration.FailureStatus);
        Assert.Contains("ready", registration.Tags);
        Assert.IsType<ObjectStorageHealthCheck>(registration.Factory(provider));
    }

    [Fact]
    public void Missing_arguments_are_rejected()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddPinqponqObjectStorage((Action<ObjectStorageOptions>)null!));
        Assert.Throws<ArgumentNullException>(() => services.AddPinqponqObjectStorage((IConfigurationSection)null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddPinqponqObjectStorage(ConfigureAws));
        Assert.Throws<ArgumentNullException>(() => ((IHealthChecksBuilder)null!).AddPinqponqObjectStorageHealthCheck());
    }

    private static void ConfigureAws(ObjectStorageOptions storageOptions)
    {
        storageOptions.BucketName = StorageOptionsFactory.BUCKET_NAME;
        storageOptions.AccessKey = StorageOptionsFactory.ACCESS_KEY;
        storageOptions.SecretKey = StorageOptionsFactory.SECRET_KEY;
        storageOptions.Region = StorageOptionsFactory.REGION;
    }
}
