using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pinqponq.LiveKit.Server.Auth;
using Pinqponq.LiveKit.Server.DependencyInjection;
using Pinqponq.LiveKit.Server.Rooms;
using Pinqponq.LiveKit.Server.Webhooks;
using Xunit;

namespace Pinqponq.LiveKit.Server.Tests.DependencyInjection;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddPinqponqLiveKitServer_ResolvesAllClients()
    {
        var services = new ServiceCollection();
        services.AddPinqponqLiveKitServer(serverOptions => serverOptions.Url = "ws://localhost:7880")
            .AddStaticCredentials(TestCredentials.API_KEY, TestCredentials.API_SECRET);

        using var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        serviceProvider.GetRequiredService<RoomServiceClient>().Should().NotBeNull();
        serviceProvider.GetRequiredService<AccessTokenIssuer>().Should().NotBeNull();
        serviceProvider.GetRequiredService<WebhookReceiver>().Should().NotBeNull();
    }

    [Fact]
    public void AddPinqponqLiveKitServer_RejectsInvalidUrl()
    {
        var services = new ServiceCollection();
        services.AddPinqponqLiveKitServer(serverOptions => serverOptions.Url = "not a url")
            .AddStaticCredentials(TestCredentials.API_KEY, TestCredentials.API_SECRET);

        using var serviceProvider = services.BuildServiceProvider();

        var act = () => serviceProvider.GetRequiredService<IOptions<LiveKitServerOptions>>().Value;

        act.Should().ThrowExactly<OptionsValidationException>();
    }
}
