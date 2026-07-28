using System.Net.Sockets;

namespace Pinqponq.Playground.Infrastructure;

/// <summary>
/// Minimal Docker daemon reachability check.
/// </summary>
/// <remarks>
/// Testcontainers does not expose a cheap "is the daemon there?" call, and starting a
/// throwaway container just to find out would cost an image pull. Hitting the daemon's
/// <c>/_ping</c> endpoint over the configured transport answers the same question in
/// milliseconds and works for both unix-socket and TCP endpoints.
/// </remarks>
public static class DockerProbe
{
    private const string DefaultUnixSocket = "unix:///var/run/docker.sock";

    /// <summary>Pings the daemon; throws when it is not reachable.</summary>
    public static async Task PingAsync(CancellationToken cancellationToken)
    {
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (string.IsNullOrWhiteSpace(dockerHost))
        {
            dockerHost = DefaultUnixSocket;
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

            case "tcp":
            case "http":
                requestUri = new Uri($"http://{endpoint.Host}:{endpoint.Port}/_ping");
                break;

            case "https":
                requestUri = new Uri($"https://{endpoint.Host}:{endpoint.Port}/_ping");
                break;

            case "npipe":
                throw new PlatformNotSupportedException(
                    "Windows named pipe uç noktası bu konsoldan yoklanamıyor; servisi elle başlatmayı deneyin.");

            default:
                throw new NotSupportedException($"Desteklenmeyen DOCKER_HOST şeması: {endpoint.Scheme}");
        }

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        using var response = await client.GetAsync(requestUri, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }
}
