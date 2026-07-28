using System.IO.Pipes;
using System.Net.Sockets;

namespace Pinqponq.Playground.Infrastructure;

/// <summary>
/// Minimal Docker daemon reachability check.
/// </summary>
/// <remarks>
/// Testcontainers does not expose a cheap "is the daemon there?" call, and starting a
/// throwaway container just to find out would cost an image pull. Hitting the daemon's
/// <c>/_ping</c> endpoint over the configured transport answers the same question in
/// milliseconds and works for unix-socket, Windows named-pipe, and TCP endpoints.
/// </remarks>
public static class DockerProbe
{
    private const string DefaultUnixSocket = "unix:///var/run/docker.sock";
    private const string DefaultWindowsNamedPipe = "npipe://./pipe/docker_engine";

    /// <summary>Pings the daemon; throws when it is not reachable.</summary>
    public static async Task PingAsync(CancellationToken cancellationToken)
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (string.IsNullOrWhiteSpace(dockerHost))
        {
            dockerHost = OperatingSystem.IsWindows()
                ? DefaultWindowsNamedPipe
                : DefaultUnixSocket;
        }

        var endpoint = new Uri(dockerHost);
        using var handler = new SocketsHttpHandler();
        Uri requestUri;

        switch (endpoint.Scheme)
        {
            case "unix":
                var socketPath = endpoint.LocalPath;
                if (!File.Exists(socketPath))
                {
                    throw new IOException($"Docker soketi bulunamadı: {socketPath}");
                }

                handler.ConnectCallback = async (_, token) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token)
                            .ConfigureAwait(false);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                };

                requestUri = new Uri("http://localhost/_ping");
                break;

            case "npipe":
                var pipeName = GetNamedPipeName(endpoint);
                handler.ConnectCallback = async (_, token) =>
                {
                    var pipe = new NamedPipeClientStream(
                        serverName: ".",
                        pipeName: pipeName,
                        direction: PipeDirection.InOut,
                        options: PipeOptions.Asynchronous);

                    try
                    {
                        await pipe.ConnectAsync(timeout: 2000, cancellationToken: token)
                            .ConfigureAwait(false);
                        return pipe;
                    }
                    catch (Exception exception)
                    {
                        await pipe.DisposeAsync().ConfigureAwait(false);
                        throw new IOException(
                            $"Docker named pipe bulunamadı: \\\\.\\pipe\\{pipeName}. Docker Desktop çalışıyor mu?",
                            exception);
                    }
                };

                requestUri = new Uri("http://localhost/_ping");
                break;

            case "tcp":
            case "http":
                requestUri = new Uri($"http://{endpoint.Host}:{endpoint.Port}/_ping");
                break;

            case "https":
                requestUri = new Uri($"https://{endpoint.Host}:{endpoint.Port}/_ping");
                break;

            default:
                throw new NotSupportedException($"Desteklenmeyen DOCKER_HOST şeması: {endpoint.Scheme}");
        }

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Extracts the pipe name from <c>npipe://./pipe/docker_engine</c> or
    /// <c>npipe:////./pipe/docker_engine</c>.
    /// </summary>
    private static string GetNamedPipeName(Uri endpoint)
    {
        const string marker = "/pipe/";
        var path = endpoint.AbsolutePath.Replace('\\', '/');
        var index = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            // Fallback: last non-empty segment (e.g. custom DOCKER_HOST shapes).
            var segment = endpoint.Segments.LastOrDefault(static s => s is not "/" and not "//");
            var candidate = segment?.Trim('/') ?? string.Empty;
            if (string.IsNullOrEmpty(candidate))
            {
                throw new NotSupportedException($"Geçersiz npipe DOCKER_HOST: {endpoint}");
            }

            return candidate;
        }

        var pipeName = path[(index + marker.Length)..].Trim('/');
        if (string.IsNullOrEmpty(pipeName))
        {
            throw new NotSupportedException($"Geçersiz npipe DOCKER_HOST: {endpoint}");
        }

        return pipeName;
    }
}
