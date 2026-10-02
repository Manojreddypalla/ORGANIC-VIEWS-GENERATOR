using System.Net;
using System.Net.Sockets;
using ProxySandboxMvp.Models;

namespace ProxySandboxMvp.Providers;

public static class ProxyParser
{
    public static Proxy? Normalize(string value, string provider)
    {
        value = value.Trim().Trim('"').TrimStart('\uFEFF');
        if (!Uri.TryCreate(value.Contains("://") ? value : $"http://{value}", UriKind.Absolute, out var uri) || uri.Host.Length == 0 || uri.Port <= 0) return null;
        if (IPAddress.TryParse(uri.Host, out var address) && (address.AddressFamily != AddressFamily.InterNetwork || address.GetAddressBytes()[0] == 0 || IPAddress.IsLoopback(address) || address.Equals(IPAddress.Broadcast))) return null;
        var userInfo = uri.UserInfo;
        var separator = userInfo.IndexOf(':');
        return new Proxy
        {
            Host = uri.Host,
            Port = uri.Port,
            Scheme = uri.Scheme,
            Username = separator >= 0 ? WebUtility.UrlDecode(userInfo[..separator]) : WebUtility.UrlDecode(userInfo),
            Password = separator >= 0 ? WebUtility.UrlDecode(userInfo[(separator + 1)..]) : null,
            Provider = provider
        };
    }

    public static IReadOnlyList<string> ParseCsv(string content)
    {
        var lines = content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return [];
        var delimiter = lines[0].Count(character => character == ';') > lines[0].Count(character => character == ',') ? ';' : lines[0].Contains('\t') ? '\t' : ',';
        var rows = lines.Select(line => ParseRow(line, delimiter)).Where(row => row.Count > 0).ToList();
        if (rows.Count == 0) return [];
        var headers = rows[0].Select(value => value.Trim().TrimStart('\uFEFF').ToLowerInvariant()).ToList();
        var hasHeaders = headers.Any(header => header is "proxy" or "endpoint" or "address" or "host" or "ip" or "protocol" or "port");
        var start = hasHeaders ? 1 : 0;
        var result = new List<string>();
        for (var rowIndex = start; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (hasHeaders)
            {
                var value = Get(row, headers, "proxy") ?? Get(row, headers, "endpoint") ?? Get(row, headers, "address");
                if (string.IsNullOrWhiteSpace(value))
                {
                    var host = Get(row, headers, "host") ?? Get(row, headers, "ip");
                    var port = Get(row, headers, "port");
                    var scheme = Get(row, headers, "scheme") ?? Get(row, headers, "protocol") ?? "http";
                    scheme = scheme.Replace("://", "").Trim();
                    var username = Get(row, headers, "username") ?? Get(row, headers, "user");
                    var password = Get(row, headers, "password") ?? Get(row, headers, "pass");
                    value = string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(port) ? null : $"{scheme}://{(string.IsNullOrWhiteSpace(username) ? "" : $"{username}:{password}@")}{host}:{port}";
                }
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value);
            }
            else result.Add(row[0]);
        }
        return result;
    }

    private static string? Get(IReadOnlyList<string> row, IReadOnlyList<string> headers, string name)
    {
        var index = -1;
        for (var headerIndex = 0; headerIndex < headers.Count; headerIndex++)
            if (headers[headerIndex] == name) { index = headerIndex; break; }
        return index >= 0 && index < row.Count ? row[index].Trim() : null;
    }

    private static IReadOnlyList<string> ParseRow(string line, char delimiter)
    {
        var values = new List<string>();
        var value = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"') { if (quoted && index + 1 < line.Length && line[index + 1] == '"') { value.Append('"'); index++; } else quoted = !quoted; }
            else if (character == delimiter && !quoted) { values.Add(value.ToString()); value.Clear(); }
            else value.Append(character);
        }
        values.Add(value.ToString());
        return values;
    }
}
