using ProxySandboxMvp.Models;
using ProxySandboxMvp.Providers;

namespace ProxySandboxMvp;

public sealed class ProxyManager(Logger logger, ProxyHealthChecker healthChecker)
{
    private readonly List<Proxy> _proxies = [];
    private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    private int _cursor;
    public int LastHealthyCount { get; private set; }
    public int LastDeadCount { get; private set; }
    public IReadOnlyList<Proxy> Proxies { get { lock (_sync) return _proxies.ToList(); } }

    public async Task RefreshAsync(IEnumerable<string> providerUrls, CancellationToken cancellationToken)
    {
        var fetched = new List<Proxy>();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        foreach (var url in providerUrls.Where(url => Uri.TryCreate(url, UriKind.Absolute, out _)))
        {
            try
            {
                await logger.InfoAsync("FetchingProxies", data: new { provider = url });
                fetched.AddRange(await new HttpProxyProvider(client, url).FetchProxiesAsync(cancellationToken));
            }
            catch (Exception ex) { await logger.WarnAsync("ProviderUnavailable", data: new { provider = url, error = ex.Message }); }
        }
        await CheckAllAsync(fetched, "providers", cancellationToken);
        lock (_sync)
        {
            var known = _proxies.Select(proxy => proxy.DisplayEndpoint).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var healthy = fetched.Where(proxy => proxy.HealthStatus is ProxyHealthStatus.Healthy or ProxyHealthStatus.Slow).ToList();
            LastHealthyCount = healthy.Count;
            LastDeadCount = fetched.Count - healthy.Count;
            _proxies.AddRange(healthy.Where(proxy => known.Add(proxy.DisplayEndpoint)));
            _cursor = 0;
        }
        await logger.InfoAsync("ProxyHealthSummary", data: new { source = "providers", checkedCount = fetched.Count, healthy = LastHealthyCount, dead = LastDeadCount });
    }

    public async Task<int> ImportCsvAsync(string filePath, CancellationToken cancellationToken)
    {
        var values = ProxyParser.ParseCsv(await File.ReadAllTextAsync(filePath, cancellationToken));
        var imported = values.Select(value => ProxyParser.Normalize(value, Path.GetFileName(filePath))).Where(proxy => proxy is not null).Cast<Proxy>().ToList();
        await CheckAllAsync(imported, Path.GetFileName(filePath), cancellationToken);
        var healthy = imported.Where(proxy => proxy.HealthStatus is ProxyHealthStatus.Healthy or ProxyHealthStatus.Slow).ToList();
        LastHealthyCount = healthy.Count;
        LastDeadCount = imported.Count - healthy.Count;
        lock (_sync)
        {
            var known = _proxies.Select(proxy => proxy.DisplayEndpoint).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _proxies.AddRange(healthy.Where(proxy => known.Add(proxy.DisplayEndpoint)));
        }
        await logger.InfoAsync("ProxyHealthSummary", data: new { source = Path.GetFileName(filePath), checkedCount = imported.Count, healthy = LastHealthyCount, dead = LastDeadCount });
        return healthy.Count;
    }

    private async Task CheckAllAsync(IReadOnlyList<Proxy> proxies, string source, CancellationToken cancellationToken)
    {
        var completed = 0;
        var healthy = 0;
        var dead = 0;
        await Task.WhenAll(proxies.Select(async proxy =>
        {
            await healthChecker.CheckAsync(proxy, cancellationToken);
            if (proxy.HealthStatus is ProxyHealthStatus.Healthy or ProxyHealthStatus.Slow) Interlocked.Increment(ref healthy);
            else Interlocked.Increment(ref dead);
            var current = Interlocked.Increment(ref completed);
            if (current % 100 == 0 || current == proxies.Count)
                await logger.InfoAsync("ProxyHealthProgress", data: new { source, checkedCount = current, total = proxies.Count, healthy, dead });
        }));
    }

    public async Task<Proxy?> AcquireAsync(IReadOnlySet<string> excluded, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            Proxy? candidate;
            lock (_sync)
            {
                var candidates = _proxies.Where(proxy => !excluded.Contains(proxy.DisplayEndpoint) && !_reserved.Contains(proxy.DisplayEndpoint) && proxy.HealthStatus is ProxyHealthStatus.Healthy or ProxyHealthStatus.Slow).ToArray();
                candidate = candidates.Length == 0 ? null : candidates[_cursor++ % candidates.Length];
                if (candidate is not null) _reserved.Add(candidate.DisplayEndpoint);
            }
            if (candidate is null) return null;
            if (await healthChecker.CheckAsync(candidate, cancellationToken)) return candidate;
            lock (_sync) _reserved.Remove(candidate.DisplayEndpoint);
        }
        return null;
    }

    public void Release(Proxy proxy)
    {
        lock (_sync) _reserved.Remove(proxy.DisplayEndpoint);
    }
}
