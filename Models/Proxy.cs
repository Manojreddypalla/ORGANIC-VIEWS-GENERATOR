namespace ProxySandboxMvp.Models;

public enum ProxyHealthStatus { Unknown, Checking, Healthy, Slow, Failed, Disabled }

public sealed class Proxy
{
    public required string Host { get; init; }
    public int Port { get; init; }
    public string Scheme { get; init; } = "http";
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string Provider { get; init; } = "manual";
    public DateTimeOffset? LastChecked { get; set; }
    public TimeSpan? Latency { get; set; }
    public ProxyHealthStatus HealthStatus { get; set; } = ProxyHealthStatus.Unknown;
    public int ConsecutiveFailures { get; set; }
    public string Endpoint => $"{Scheme}://{Host}:{Port}";
    public string DisplayEndpoint => $"{Host}:{Port}";
}
