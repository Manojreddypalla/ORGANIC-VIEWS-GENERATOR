using System.Collections.ObjectModel;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;
using ProxySandboxMvp.Models;

namespace ProxySandboxMvp;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<LogLine> _logEntries = [];
    private readonly Logger _logger = new();
    private AppConfig _config = new();
    private ProxyManager? _proxyManager;
    private EmbeddedBrowserSessionManager? _sessionManager;
    private BrowserWindow? _browserWindow;
    private CancellationTokenSource _appCancellation = new();
    private readonly DispatcherTimer _sessionSummaryTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public MainWindow()
    {
        InitializeComponent();
        LogList.ItemsSource = _logEntries;
        _sessionSummaryTimer.Tick += (_, _) => UpdateSessionSummary();
        _sessionSummaryTimer.Start();
        _logger.EntryAdded += entry => Dispatcher.Invoke(() =>
        {
            var level = entry.Contains("ERROR", StringComparison.OrdinalIgnoreCase) ? "ERROR" : entry.Contains("WARN", StringComparison.OrdinalIgnoreCase) ? "WARN" : "INFO";
            _logEntries.Add(new LogLine(entry, level));
            if (_logEntries.Count > 200) _logEntries.RemoveAt(0);
        });
        Loaded += async (_, _) => await LoadConfigAsync();
        Closing += async (_, _) =>
        {
            _appCancellation.Cancel();
            _sessionSummaryTimer.Stop();
            if (_sessionManager is not null) await _sessionManager.StopAsync();
            _browserWindow?.Close();
            await _logger.ClearSessionLogsAsync();
        };
    }

    private async Task LoadConfigAsync()
    {
        _config = await ConfigStore.LoadAsync();
        SessionCountBox.Text = _config.SessionCount.ToString();
        RotationMinutesBox.Text = Math.Clamp(_config.RotationMinutes, 1, 5).ToString();
        ProviderUrlBox.Text = string.Join(Environment.NewLine, _config.ProviderUrls);
        BuildServices();
        await _logger.InfoAsync("ApplicationStarted");
    }

    private void BuildServices()
    {
        _proxyManager = new ProxyManager(_logger, new ProxyHealthChecker(_config.ProxyHealthTimeoutSeconds));
        _browserWindow ??= new BrowserWindow();
        _sessionManager = new EmbeddedBrowserSessionManager(_config, _proxyManager, _logger, OpenBrowserAsync, CloseBrowserAsync);
    }

    private void UpdateSessionSummary()
    {
        var sessions = _sessionManager?.Sessions ?? [];
        var successful = sessions.Sum(session => session.SuccessfulViews);
        var running = sessions.Count(session => session.Status == SessionStatus.Running);
        var failed = sessions.Count(session => session.Status == SessionStatus.Failed);
        SessionSummary.Text = $"Successful views: {successful}   Active: {running}   Failed: {failed}";
    }

    private async Task OpenBrowserAsync(SessionInfo info, Proxy proxy, string targetUrl, CancellationToken cancellationToken)
    {
        await _browserWindow!.OpenSessionAsync(info, proxy, targetUrl, cancellationToken);
    }

    private async Task CloseBrowserAsync(int sessionId)
    {
        if (_browserWindow is not null) await _browserWindow.CloseSessionAsync(sessionId);
    }

    private void OpenBrowserMatrix_Click(object sender, RoutedEventArgs e)
    {
        _browserWindow ??= new BrowserWindow();
        _browserWindow.ShowMatrix();
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _config.SessionCount = Math.Clamp(int.Parse(SessionCountBox.Text), 1, 50);
            _config.RotationMinutes = Math.Clamp(int.TryParse(RotationMinutesBox.Text, out var rotationMinutes) ? rotationMinutes : 5, 1, 5);
            _config.ProviderUrls = ProviderUrlBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            if (_proxyManager is null || _sessionManager is null) BuildServices();
            if (_proxyManager!.Proxies.Count == 0) await RefreshProxiesAsync();
            await _sessionManager!.StartAsync(TargetUrlBox.Text.Trim(), _appCancellation.Token);
            SessionsGrid.ItemsSource = _sessionManager.Sessions;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Unable to start", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_sessionManager is not null) await _sessionManager.StopAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        try { await RefreshProxiesAsync(); }
        catch (Exception ex) { await _logger.WarnAsync("ProxyRefreshFailed", data: new { error = ex.Message }); }
    }

    private async void ImportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_proxyManager is null) return;
        var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        try
        {
            PoolSummary.Text = "Checking proxies...";
            var imported = await _proxyManager.ImportCsvAsync(dialog.FileName, _appCancellation.Token);
            PoolSummary.Text = $"Healthy: {_proxyManager.LastHealthyCount}   Dead: {_proxyManager.LastDeadCount}   Pool: {_proxyManager.Proxies.Count}";
            await _logger.InfoAsync("CsvImportCompleted", data: new { file = dialog.SafeFileName, imported });
        }
        catch (Exception ex)
        {
            await _logger.WarnAsync("CsvImportFailed", data: new { file = dialog.SafeFileName, error = ex.Message });
            MessageBox.Show(ex.Message, "Unable to import CSV", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ExportLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var source = Path.Combine("logs", $"application-{DateTime.Now:yyyy-MM-dd}.jsonl");
            if (!File.Exists(source))
            {
                MessageBox.Show("No log file exists for today yet.", "Export logs", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            var destination = Path.Combine(downloads, $"proxy-sandbox-log-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            File.Copy(source, destination, overwrite: false);
            await _logger.InfoAsync("LogsExported", data: new { file = destination });
            MessageBox.Show($"Log exported to:\n{destination}", "Export logs", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            await _logger.WarnAsync("LogsExportFailed", data: new { error = ex.Message });
            MessageBox.Show(ex.Message, "Unable to export logs", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task RefreshProxiesAsync()
    {
        if (_proxyManager is null) return;
        PoolSummary.Text = "Checking proxies...";
        await _proxyManager.RefreshAsync(ProviderUrlBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), _appCancellation.Token);
        PoolSummary.Text = $"Healthy: {_proxyManager.LastHealthyCount}   Dead: {_proxyManager.LastDeadCount}   Pool: {_proxyManager.Proxies.Count}";
    }
}

public sealed record LogLine(string Text, string Level);
