using System.Net.Http;
using System.Text.Json;
using ProxySandboxMvp.Models;

namespace ProxySandboxMvp.Providers;

public sealed class HttpProxyProvider(HttpClient httpClient, string url) : IProxyProvider
{
    public async Task<IReadOnlyList<Proxy>> FetchProxiesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Parse(body, url);
    }

    private static IReadOnlyList<Proxy> Parse(string body, string provider)
    {
        var values = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
                values.AddRange(document.RootElement.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))!);
            else if (document.RootElement.TryGetProperty("proxies", out var proxies) && proxies.ValueKind == JsonValueKind.Array)
                values.AddRange(proxies.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x))!);
        }
        catch (JsonException) { values.AddRange(body.Split(new[] { '\r', '\n', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)); }

        return values.Select(value => ProxyParser.Normalize(value!, provider)).Where(proxy => proxy is not null).Cast<Proxy>().ToList();
    }
}
