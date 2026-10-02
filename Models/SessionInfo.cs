using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProxySandboxMvp.Models;

public enum SessionStatus { Stopped, Starting, CheckingProxy, Running, Rotating, Waiting, Failed }

public sealed class SessionInfo : INotifyPropertyChanged
{
    private SessionStatus _status = SessionStatus.Stopped;
    private Proxy? _proxy;
    private DateTimeOffset? _started;
    private DateTimeOffset? _nextRotation;
    private int _errors;
    private int _successfulViews;
    private string? _message;

    public int Id { get; init; }
    public SessionStatus Status { get => _status; set => Set(ref _status, value); }
    public Proxy? Proxy { get => _proxy; set => Set(ref _proxy, value); }
    public DateTimeOffset? Started { get => _started; set => Set(ref _started, value); }
    public DateTimeOffset? NextRotation { get => _nextRotation; set => Set(ref _nextRotation, value); }
    public int Errors { get => _errors; set => Set(ref _errors, value); }
    public int SuccessfulViews { get => _successfulViews; set => Set(ref _successfulViews, value); }
    public string? Message { get => _message; set => Set(ref _message, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
