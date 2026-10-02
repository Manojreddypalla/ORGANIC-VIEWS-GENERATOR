using ProxySandboxMvp.Models;

namespace ProxySandboxMvp;

public sealed class EmbeddedBrowserSessionManager(
    AppConfig config,
    ProxyManager proxyManager,
    Logger logger,
    Func<SessionInfo, Proxy, string, CancellationToken, Task> openBrowser,
    Func<int, Task> closeBrowser)
{
    private readonly Random _random = new();
    private readonly List<(SessionInfo Info, CancellationTokenSource Cts, Task Task)> _running = [];
    public IReadOnlyList<SessionInfo> Sessions { get; private set; } = [];

    public async Task StartAsync(string targetUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(targetUrl, UriKind.Absolute, out var target) || target.Scheme is not ("http" or "https")) throw new ArgumentException("Enter a valid http or https URL.");
        Sessions = Enumerable.Range(1, Math.Max(1, config.SessionCount)).Select(id => new SessionInfo { Id = id }).ToList();
        foreach (var info in Sessions)
        {
            var sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var task = RunSessionAsync(info, target.ToString(), sessionCancellation.Token);
            lock (_running) _running.Add((info, sessionCancellation, task));
        }
        await logger.InfoAsync("SessionsStarting", data: new { count = Sessions.Count, target = target.ToString() });
    }

    private async Task RunSessionAsync(SessionInfo info, string targetUrl, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && info.Errors <= 10)
        {
            try
            {
                info.Status = SessionStatus.CheckingProxy;
                var excluded = Sessions.Where(session => session.Id != info.Id && session.Proxy is not null).Select(session => session.Proxy!.DisplayEndpoint).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var proxy = await proxyManager.AcquireAsync(excluded, cancellationToken);
                if (proxy is null) throw new InvalidOperationException("No healthy replacement proxy is available.");
                info.Proxy = proxy;
                info.Started ??= DateTimeOffset.Now;
                info.Status = SessionStatus.Starting;
                await openBrowser(info, proxy, targetUrl, cancellationToken);
                info.SuccessfulViews++;
                info.Status = SessionStatus.Running;
                await logger.InfoAsync("SessionStarted", info.Id, new { proxy = proxy.DisplayEndpoint });

                while (!cancellationToken.IsCancellationRequested)
                {
                    var delay = TimeSpan.FromMinutes(config.RotationMinutes) + TimeSpan.FromSeconds(_random.Next(0, Math.Max(1, config.RotationJitterSeconds + 1)));
                    info.NextRotation = DateTimeOffset.Now.Add(delay);
                    await Task.Delay(delay, cancellationToken);
                    info.Status = SessionStatus.Rotating;
                    var replacementExcluded = Sessions.Where(session => session.Id != info.Id && session.Proxy is not null).Select(session => session.Proxy!.DisplayEndpoint).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var replacement = await proxyManager.AcquireAsync(replacementExcluded, cancellationToken);
                    if (replacement is null) { info.Status = SessionStatus.Waiting; continue; }
                    var previous = info.Proxy;
                    await closeBrowser(info.Id);
                    if (previous is not null) proxyManager.Release(previous);
                    info.Proxy = replacement;
                    await openBrowser(info, replacement, targetUrl, cancellationToken);
                    info.SuccessfulViews++;
                    info.Status = SessionStatus.Running;
                    await logger.InfoAsync("ProxyRotated", info.Id, new { proxy = replacement.DisplayEndpoint });
                }
                return;
            }
            catch (OperationCanceledException) { info.Status = SessionStatus.Stopped; return; }
            catch (Exception ex)
            {
                info.Errors++;
                info.Message = ex.Message;
                if (info.Proxy is not null)
                {
                    info.Proxy.HealthStatus = ProxyHealthStatus.Failed;
                    proxyManager.Release(info.Proxy);
                    info.Proxy = null;
                }
                if (info.Errors > 10) break;
                info.Status = SessionStatus.Rotating;
                await logger.WarnAsync("SessionRetryingWithReplacement", info.Id, new { error = ex.Message, attempt = info.Errors });
                await closeBrowser(info.Id);
            }
        }
        info.Status = cancellationToken.IsCancellationRequested ? SessionStatus.Stopped : SessionStatus.Failed;
        await logger.WarnAsync("SessionFailed", info.Id, new { error = info.Message, attempts = info.Errors });
    }

    public async Task StopAsync()
    {
        (SessionInfo Info, CancellationTokenSource Cts, Task Task)[] running;
        lock (_running) running = _running.ToArray();
        foreach (var session in running) session.Cts.Cancel();
        foreach (var session in running) { try { await closeBrowser(session.Info.Id); } catch { } }
        try { await Task.WhenAll(running.Select(session => session.Task)); } catch { }
        foreach (var info in Sessions) info.Status = SessionStatus.Stopped;
        lock (_running) _running.Clear();
    }
}
