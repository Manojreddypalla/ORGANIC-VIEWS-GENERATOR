using ProxySandboxMvp.Models;

namespace ProxySandboxMvp.Providers;

public interface IProxyProvider
{
    Task<IReadOnlyList<Proxy>> FetchProxiesAsync(CancellationToken cancellationToken);
}
