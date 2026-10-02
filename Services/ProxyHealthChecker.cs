using System.Diagnostics;
using System.Net.Sockets;
using ProxySandboxMvp.Models;

namespace ProxySandboxMvp;

public sealed class ProxyHealthChecker(int timeoutSeconds)
{
    public async Task<bool> CheckAsync(Proxy proxy, CancellationToken cancellationToken)
    {
        proxy.HealthStatus = ProxyHealthStatus.Checking;
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var checkToken = timeout.Token;
        try
        {
            if (proxy.Scheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase))
            {
            await CheckSocksAsync(proxy, checkToken);
                stopwatch.Stop();
                proxy.Latency = stopwatch.Elapsed;
                proxy.LastChecked = DateTimeOffset.Now;
                proxy.ConsecutiveFailures = 0;
                proxy.HealthStatus = proxy.Latency > TimeSpan.FromSeconds(2) ? ProxyHealthStatus.Slow : ProxyHealthStatus.Healthy;
                return true;
            }
            var webProxy = new System.Net.WebProxy(proxy.Endpoint);
            if (!string.IsNullOrWhiteSpace(proxy.Username)) webProxy.Credentials = new System.Net.NetworkCredential(proxy.Username, proxy.Password);
            using var client = new HttpClient(new HttpClientHandler { Proxy = webProxy, UseProxy = true }) { Timeout = TimeSpan.FromSeconds(timeoutSeconds) };
            using var response = await client.GetAsync("https://example.com", checkToken);
            stopwatch.Stop();
            proxy.Latency = stopwatch.Elapsed;
            proxy.LastChecked = DateTimeOffset.Now;
            proxy.ConsecutiveFailures = 0;
            proxy.HealthStatus = proxy.Latency > TimeSpan.FromSeconds(2) ? ProxyHealthStatus.Slow : ProxyHealthStatus.Healthy;
            // A response proves the proxy connected; the destination status is not a proxy health signal.
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            proxy.LastChecked = DateTimeOffset.Now;
            proxy.ConsecutiveFailures++;
            proxy.HealthStatus = ProxyHealthStatus.Failed;
            return false;
        }
    }

    private static async Task CheckSocksAsync(Proxy proxy, CancellationToken cancellationToken)
    {
        using var socket = new TcpClient();
        await socket.ConnectAsync(proxy.Host, proxy.Port, cancellationToken);
        await using var stream = socket.GetStream();
        if (proxy.Scheme.Equals("socks5", StringComparison.OrdinalIgnoreCase))
        {
            await stream.WriteAsync(new byte[] { 5, 1, 0 }, cancellationToken);
            var response = await ReadExactlyAsync(stream, 2, cancellationToken);
            if (response[0] != 5 || response[1] == 255) throw new IOException("SOCKS5 handshake was rejected.");
            return;
        }

        var request = new byte[] { 4, 1, 0x01, 0xBB, 93, 184, 216, 34, 0 };
        await stream.WriteAsync(request, cancellationToken);
        var socks4Response = await ReadExactlyAsync(stream, 8, cancellationToken);
        if (socks4Response[1] != 90) throw new IOException($"SOCKS4 handshake was rejected with status {socks4Response[1]}.");
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length, CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0) throw new IOException("Proxy closed the connection during handshake.");
            offset += read;
        }
        return buffer;
    }
}
