using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Pinqponq.TestSupport.Fixtures;

/// <summary>MailHog SMTP + HTTP API container for mail integration tests.</summary>
public class MailHogFixture 
{
    private const ushort SmtpPort = 1025;
    private const ushort HttpPort = 8025;

    private readonly IContainer _container = new ContainerBuilder("mailhog/mailhog:v1.0.1")
        .WithPortBinding(SmtpPort, true)
        .WithPortBinding(HttpPort, true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r
            .ForPort(HttpPort)
            .ForPath("/api/v2/messages")))
        .Build();

    /// <summary>SMTP host (localhost).</summary>
    public string SmtpHost => _container.Hostname;

    /// <summary>Mapped SMTP port.</summary>
    public int SmtpMappedPort => _container.GetMappedPublicPort(SmtpPort);

    /// <summary>Base URL for MailHog HTTP API.</summary>
    public string ApiBaseUrl =>
        $"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}";

    public async Task InitializeAsync() => await _container.StartAsync().ConfigureAwait(false);

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);
}
