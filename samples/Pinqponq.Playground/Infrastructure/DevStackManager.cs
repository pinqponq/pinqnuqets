using System.Diagnostics;
using System.Threading.Channels;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Options;
using Testcontainers.MongoDb;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Pinqponq.Playground.Infrastructure;

/// <summary>
/// Starts, stops and tracks the backing services the packages talk to.
/// </summary>
/// <remarks>
/// Nothing is provisioned at application start: the console must be reachable
/// immediately, and pulling six images on boot would be both slow and presumptuous.
/// Each service is started on demand from the UI.
/// </remarks>
public sealed class DevStackManager : IAsyncDisposable
{
    private readonly Dictionary<string, ServiceSlot> _slots;
    private readonly ILogger<DevStackManager> _logger;
    private readonly List<Channel<DevServiceStatus>> _subscribers = [];
    private readonly object _subscriberGate = new();

    private bool? _dockerAvailable;
    private string? _dockerError;

    public DevStackManager(
        IOptions<PlaygroundOptions> options,
        ILogger<DevStackManager> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var external = options.Value.ExternalServices;
        _slots = Definitions()
            .ToDictionary(
                definition => definition.Id,
                definition => new ServiceSlot(definition, Lookup(external, definition.Id)),
                StringComparer.Ordinal);
    }

    /// <summary>Whether a Docker daemon answered the last probe.</summary>
    public bool DockerAvailable => _dockerAvailable ?? false;

    /// <summary>Why the Docker probe failed, if it did.</summary>
    public string? DockerError => _dockerError;

    /// <summary>Current snapshot of every service.</summary>
    public IReadOnlyList<DevServiceStatus> GetAll() =>
        [.. _slots.Values.Select(slot => slot.ToStatus(_dockerAvailable))];

    /// <summary>Current snapshot of one service.</summary>
    public DevServiceStatus Get(string id) =>
        _slots.TryGetValue(id, out var slot)
            ? slot.ToStatus(_dockerAvailable)
            : throw new KeyNotFoundException($"Unknown service: {id}");

    /// <summary>Pings the Docker daemon and caches the result.</summary>
    public async Task<bool> ProbeDockerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));

            await DockerProbe.PingAsync(timeout.Token).ConfigureAwait(false);
            _dockerAvailable = true;
            _dockerError = null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException
                                          || !cancellationToken.IsCancellationRequested)
        {
            _dockerAvailable = false;
            _dockerError = exception.Message;
            _logger.LogWarning(
                "Could not reach the Docker daemon; scenarios that need a container are unavailable. {Reason}",
                exception.Message);
        }

        return _dockerAvailable ?? false;
    }

    /// <summary>Starts a service if it is not already running.</summary>
    public async Task<DevServiceStatus> StartAsync(string id, CancellationToken cancellationToken = default)
    {
        var slot = Slot(id);
        if (slot.External is not null)
        {
            return slot.ToStatus(_dockerAvailable);
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.State is DevServiceState.Ready or DevServiceState.Starting)
            {
                return slot.ToStatus(_dockerAvailable);
            }

            if (_dockerAvailable is not true)
            {
                await ProbeDockerAsync(cancellationToken).ConfigureAwait(false);
                if (_dockerAvailable is not true)
                {
                    slot.State = DevServiceState.Failed;
                    slot.LastError = $"Cannot reach the Docker daemon: {_dockerError}";
                    Publish(slot);
                    return slot.ToStatus(_dockerAvailable);
                }
            }

            slot.State = DevServiceState.Starting;
            slot.LastError = null;
            Publish(slot);

            _logger.LogInformation(
                "Starting {Service} ({Image}). The image may be pulled on first run.",
                slot.Definition.DisplayName,
                slot.Definition.Image);

            var stopwatch = Stopwatch.StartNew();
            var container = slot.Definition.Create();
            await container.StartAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            slot.Container = container;
            slot.Endpoint = slot.Definition.Resolve(container);
            slot.State = DevServiceState.Ready;
            slot.StartedAt = DateTimeOffset.UtcNow;
            slot.StartupMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation(
                "{Service} ready ({ElapsedMs} ms).",
                slot.Definition.DisplayName,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception exception)
        {
            slot.State = DevServiceState.Failed;
            slot.LastError = exception.Message;
            slot.Container = null;
            slot.Endpoint = null;
            _logger.LogError(exception, "Failed to start {Service}.", slot.Definition.DisplayName);
        }
        finally
        {
            slot.Gate.Release();
        }

        Publish(slot);
        return slot.ToStatus(_dockerAvailable);
    }

    /// <summary>Stops and removes a service's container.</summary>
    public async Task<DevServiceStatus> StopAsync(string id, CancellationToken cancellationToken = default)
    {
        var slot = Slot(id);
        if (slot.External is not null)
        {
            return slot.ToStatus(_dockerAvailable);
        }

        await slot.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (slot.Container is { } container)
            {
                await container.DisposeAsync().ConfigureAwait(false);
                _logger.LogInformation("{Service} stopped.", slot.Definition.DisplayName);
            }

            slot.Container = null;
            slot.Endpoint = null;
            slot.StartedAt = null;
            slot.StartupMs = null;
            slot.State = DevServiceState.Stopped;
        }
        catch (Exception exception)
        {
            slot.State = DevServiceState.Failed;
            slot.LastError = exception.Message;
            _logger.LogError(exception, "Failed to stop {Service}.", slot.Definition.DisplayName);
        }
        finally
        {
            slot.Gate.Release();
        }

        Publish(slot);
        return slot.ToStatus(_dockerAvailable);
    }

    /// <summary>Stops then starts a service — used to prove retry and health-check behaviour.</summary>
    public async Task<DevServiceStatus> RestartAsync(string id, CancellationToken cancellationToken = default)
    {
        await StopAsync(id, cancellationToken).ConfigureAwait(false);
        return await StartAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolved endpoint of a ready service, or a friendly failure.</summary>
    public DevEndpoint Require(string id)
    {
        var slot = Slot(id);
        if (slot.External is { } external)
        {
            return external;
        }

        if (slot.State != DevServiceState.Ready || slot.Endpoint is null)
        {
            throw new DevStackNotReadyException(
                $"'{slot.Definition.DisplayName}' is not ready. Start it from the top strip and try again.");
        }

        return slot.Endpoint;
    }

    /// <summary>Connection string of a ready service.</summary>
    public string RequireConnectionString(string id)
    {
        var endpoint = Require(id);
        return endpoint.ConnectionString
               ?? throw new DevStackNotReadyException($"Could not resolve a connection string for '{id}'.");
    }

    /// <summary>Whether a scenario's prerequisites are satisfied right now.</summary>
    public bool IsReady(string id) =>
        _slots.TryGetValue(id, out var slot)
        && (slot.External is not null || slot.State == DevServiceState.Ready);

    /// <summary>Subscribes to service state transitions.</summary>
    public DevStackSubscription Subscribe()
    {
        var channel = Channel.CreateBounded<DevServiceStatus>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        lock (_subscriberGate)
        {
            _subscribers.Add(channel);
        }

        return new DevStackSubscription(channel.Reader, () =>
        {
            lock (_subscriberGate)
            {
                _subscribers.Remove(channel);
            }

            channel.Writer.TryComplete();
        });
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var slot in _slots.Values)
        {
            if (slot.Container is { } container)
            {
                try
                {
                    await container.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "An error occurred while shutting down {Service}.",
                        slot.Definition.DisplayName);
                }
            }

            slot.Gate.Dispose();
        }
    }

    private ServiceSlot Slot(string id) =>
        _slots.TryGetValue(id, out var slot)
            ? slot
            : throw new KeyNotFoundException($"Unknown service: {id}");

    private void Publish(ServiceSlot slot)
    {
        var status = slot.ToStatus(_dockerAvailable);
        Channel<DevServiceStatus>[] targets;
        lock (_subscriberGate)
        {
            targets = [.. _subscribers];
        }

        foreach (var channel in targets)
        {
            channel.Writer.TryWrite(status);
        }
    }

    private static DevEndpoint? Lookup(IReadOnlyDictionary<string, string>? external, string id)
    {
        if (external is null
            || !external.TryGetValue(id, out var value)
            || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // MailHog is not addressed by a connection string; it needs host/port/API url.
        if (string.Equals(id, DevServiceIds.MailHog, StringComparison.Ordinal))
        {
            var parts = value.Split(':', StringSplitOptions.TrimEntries);
            var host = parts[0];
            var port = parts.Length > 1 && int.TryParse(parts[1], out var parsed) ? parsed : 1025;
            var apiPort = parts.Length > 2 && int.TryParse(parts[2], out var parsedApi) ? parsedApi : 8025;
            return new DevEndpoint(null, host, port, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["apiBaseUrl"] = $"http://{host}:{apiPort}",
            });
        }

        if (string.Equals(id, DevServiceIds.RabbitMq, StringComparison.Ordinal))
        {
            var uri = new Uri(value);
            var userInfo = uri.UserInfo.Split(':', 2);
            return new DevEndpoint(value, uri.Host, uri.Port, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["userName"] = userInfo.Length > 0 && userInfo[0].Length > 0 ? userInfo[0] : "guest",
                ["password"] = userInfo.Length > 1 && userInfo[1].Length > 0 ? userInfo[1] : "guest",
            });
        }

        return DevEndpoint.FromConnectionString(value);
    }

    private static IEnumerable<ServiceDefinition> Definitions()
    {
        yield return new ServiceDefinition(
            DevServiceIds.Postgres,
            "PostgreSQL",
            "Pinqponq.Database.Postgres connection, retry, and health-check scenarios.",
            DevStackImages.Postgres,
            Heavy: false,
            () => new PostgreSqlBuilder(DevStackImages.Postgres).Build(),
            container => DevEndpoint.FromConnectionString(((PostgreSqlContainer)container).GetConnectionString()));

        yield return new ServiceDefinition(
            DevServiceIds.Redis,
            "Redis",
            "Pinqponq.Cache get/set, distributed lock, and health-check scenarios.",
            DevStackImages.Redis,
            Heavy: false,
            () => new RedisBuilder(DevStackImages.Redis).Build(),
            container => DevEndpoint.FromConnectionString(((RedisContainer)container).GetConnectionString()));

        yield return new ServiceDefinition(
            DevServiceIds.RabbitMq,
            "RabbitMQ",
            "Pinqponq.Messaging.RabbitMq publish/consume and dead-letter scenarios.",
            DevStackImages.RabbitMq,
            Heavy: false,
            () => new RabbitMqBuilder(DevStackImages.RabbitMq)
                .WithUsername("guest")
                .WithPassword("guest")
                .Build(),
            container => new DevEndpoint(
                null,
                container.Hostname,
                container.GetMappedPublicPort(5672),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["userName"] = "guest",
                    ["password"] = "guest",
                }));

        yield return new ServiceDefinition(
            DevServiceIds.Mongo,
            "MongoDB",
            "Pinqponq.Database.Mongo connection, ping, and health-check scenarios.",
            DevStackImages.Mongo,
            Heavy: false,
            () => new MongoDbBuilder(DevStackImages.Mongo).Build(),
            container => DevEndpoint.FromConnectionString(((MongoDbContainer)container).GetConnectionString()));

        yield return new ServiceDefinition(
            DevServiceIds.MailHog,
            "MailHog (SMTP)",
            "Pinqponq.Mail sending and the OTP email channel; the inbox is visible in the UI.",
            DevStackImages.MailHog,
            Heavy: false,
            () => new ContainerBuilder(DevStackImages.MailHog)
                .WithPortBinding(1025, true)
                .WithPortBinding(8025, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request
                    .ForPort(8025)
                    .ForPath("/api/v2/messages")))
                .Build(),
            container => new DevEndpoint(
                null,
                container.Hostname,
                container.GetMappedPublicPort(1025),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["apiBaseUrl"] = $"http://{container.Hostname}:{container.GetMappedPublicPort(8025)}",
                }));

        yield return new ServiceDefinition(
            DevServiceIds.MsSql,
            "SQL Server",
            "Pinqponq.Database.Mssql scenarios. Heavy image (~1.5 GB), no ARM64 support.",
            DevStackImages.MsSql,
            Heavy: true,
            () => new MsSqlBuilder(DevStackImages.MsSql).Build(),
            container => DevEndpoint.FromConnectionString(((MsSqlContainer)container).GetConnectionString()));
    }

    private sealed record ServiceDefinition(
        string Id,
        string DisplayName,
        string Description,
        string Image,
        bool Heavy,
        Func<IContainer> Create,
        Func<IContainer, DevEndpoint> Resolve);

    private sealed class ServiceSlot(ServiceDefinition definition, DevEndpoint? external)
    {
        public ServiceDefinition Definition { get; } = definition;

        public DevEndpoint? External { get; } = external;

        public SemaphoreSlim Gate { get; } = new(1, 1);

        public IContainer? Container { get; set; }

        public DevEndpoint? Endpoint { get; set; }

        public DevServiceState State { get; set; } =
            external is not null ? DevServiceState.External : DevServiceState.Stopped;

        public string? LastError { get; set; }

        public DateTimeOffset? StartedAt { get; set; }

        public long? StartupMs { get; set; }

        public DevServiceStatus ToStatus(bool? dockerAvailable)
        {
            var endpoint = External ?? Endpoint;
            var state = State;
            if (External is null && dockerAvailable is false && state == DevServiceState.Stopped)
            {
                state = DevServiceState.DockerUnavailable;
            }

            return new DevServiceStatus
            {
                Id = Definition.Id,
                DisplayName = Definition.DisplayName,
                Description = Definition.Description,
                Image = Definition.Image,
                Heavy = Definition.Heavy,
                State = state,
                ConnectionString = endpoint?.ConnectionString,
                Host = endpoint?.Host,
                Port = endpoint?.Port,
                StartedAt = StartedAt,
                StartupMs = StartupMs,
                LastError = LastError,
                ContainerId = Container?.Id,
            };
        }
    }
}

/// <summary>A live dev-stack subscription; dispose to unsubscribe.</summary>
public sealed class DevStackSubscription(ChannelReader<DevServiceStatus> reader, Action onDispose) : IDisposable
{
    /// <summary>Reader delivering status transitions.</summary>
    public ChannelReader<DevServiceStatus> Reader { get; } = reader;

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        onDispose();
    }
}
