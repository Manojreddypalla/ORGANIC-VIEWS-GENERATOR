namespace ProxySandboxMvp.Models;

public sealed class AppConfig
{
    public int SessionCount { get; set; } = 6;
    public int RotationMinutes { get; set; } = 5;
    public int RotationJitterSeconds { get; set; } = 30;
    public int ProxyHealthTimeoutSeconds { get; set; } = 10;
    public int ProviderRefreshMinutes { get; set; } = 5;
    public List<string> ProviderUrls { get; set; } = [];
    public bool Headless { get; set; }
}
