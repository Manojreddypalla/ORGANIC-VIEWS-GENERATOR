using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ProxySandboxMvp.Models;
using System.Windows;
using System.Windows.Controls;

namespace ProxySandboxMvp;

public partial class BrowserWindow : Window
{
    private readonly Dictionary<int, WebView2> _views = [];

    public BrowserWindow()
    {
        InitializeComponent();
        Closing += (_, args) => { args.Cancel = true; Hide(); };
    }

    public async Task OpenSessionAsync(SessionInfo info, Proxy proxy, string targetUrl, CancellationToken cancellationToken)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            await CloseSessionAsync(info.Id);
            var profile = Path.Combine(Path.GetTempPath(), "ProxySandboxMvp", $"session-{info.Id}");
            Directory.CreateDirectory(profile);
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = $"--proxy-server={proxy.Endpoint}" };
            var environment = await CoreWebView2Environment.CreateAsync(null, profile, options);
            var view = new WebView2 { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            var tab = new TabItem { Header = $"S{info.Id}  //  {proxy.DisplayEndpoint}", Content = view };
            BrowserTabs.Items.Add(tab);
            BrowserTabs.SelectedItem = tab;
            _views[info.Id] = view;
            await view.EnsureCoreWebView2Async(environment);
            view.CoreWebView2.IsMuted = true;
            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                view.NavigationCompleted -= Completed;
                if (args.IsSuccess) navigation.TrySetResult(true);
                else navigation.TrySetException(new InvalidOperationException($"Embedded browser navigation failed: {args.WebErrorStatus}."));
            }
            view.NavigationCompleted += Completed;
            using var registration = cancellationToken.Register(() => navigation.TrySetCanceled(cancellationToken));
            view.Source = new Uri(targetUrl);
            await navigation.Task;
        }).Task.Unwrap();
    }

    public async Task CloseSessionAsync(int sessionId)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (!_views.Remove(sessionId, out var view)) return;
            var tab = BrowserTabs.Items.OfType<TabItem>().FirstOrDefault(item => ReferenceEquals(item.Content, view));
            if (tab is not null) BrowserTabs.Items.Remove(tab);
            view.Dispose();
        });
    }

    public void ShowMatrix()
    {
        Show();
        Activate();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();
    private void Close_Click(object sender, RoutedEventArgs e) => Hide();
    private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left) DragMove();
    }
}
