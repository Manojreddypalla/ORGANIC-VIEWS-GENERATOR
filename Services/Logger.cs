using System.Text.Json;

namespace ProxySandboxMvp;

public sealed class Logger
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    public event Action<string>? EntryAdded;
    public Task ClearSessionLogsAsync()
    {
        if (!Directory.Exists("logs")) return Task.CompletedTask;
        foreach (var file in Directory.EnumerateFiles("logs", "session-*.jsonl"))
            File.Delete(file);
        return Task.CompletedTask;
    }

    public async Task InfoAsync(string eventName, int? session = null, object? data = null) => await WriteAsync("INFO", eventName, session, data);
    public async Task WarnAsync(string eventName, int? session = null, object? data = null) => await WriteAsync("WARN", eventName, session, data);
    private async Task WriteAsync(string level, string eventName, int? session, object? data)
    {
        var entry = new Dictionary<string, object?> { ["timestamp"] = DateTimeOffset.Now, ["level"] = level, ["event"] = eventName };
        if (session.HasValue) entry["session"] = session.Value;
        if (data is not null) foreach (var property in data.GetType().GetProperties()) entry[property.Name] = property.GetValue(data);
        var line = JsonSerializer.Serialize(entry);
        await _gate.WaitAsync();
        try
        {
            Directory.CreateDirectory("logs");
            await File.AppendAllTextAsync(Path.Combine("logs", $"application-{DateTime.Now:yyyy-MM-dd}.jsonl"), line + Environment.NewLine);
            if (session.HasValue)
                await File.AppendAllTextAsync(Path.Combine("logs", $"session-{session.Value}-{DateTime.Now:yyyy-MM-dd}.jsonl"), line + Environment.NewLine);
        }
        finally { _gate.Release(); }
        EntryAdded?.Invoke($"{DateTime.Now:HH:mm:ss}  {level,-4}  {eventName}{(session.HasValue ? $"  [Session {session}]" : "")}");
    }
}
