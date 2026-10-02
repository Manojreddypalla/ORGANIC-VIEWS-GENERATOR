using System.Text.Json;
using ProxySandboxMvp.Models;

namespace ProxySandboxMvp;

public static class ConfigStore
{
    public static async Task<AppConfig> LoadAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", "config.json");
        if (!File.Exists(path)) return new AppConfig();
        return JsonSerializer.Deserialize<AppConfig>(await File.ReadAllTextAsync(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppConfig();
    }
}
