# Proxy Sandbox Documentation

## Purpose

Proxy Sandbox is a Windows WPF QA tool for authorized testing of a target URL through isolated WebView2 browser sessions. Each session receives an independent proxy, browser profile, session state, and session log stream.

The tool is not intended for artificial traffic generation, view manipulation, detection evasion, or testing systems without permission.

## Requirements

- Windows 10 or Windows 11
- Microsoft Edge WebView2 Runtime
- .NET 8 SDK for source builds
- Authorized target URL and proxy sources

The published EXE is self-contained and does not require the .NET SDK, but it still requires the WebView2 Runtime.

## Build From Source

```powershell
dotnet restore
dotnet build
```

Run the application with:

```powershell
dotnet run
```

## Build The Windows EXE

Create a self-contained single-file Windows build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None
```

The resulting executable is:

```text
bin/Release/net8.0-windows/win-x64/publish/ProxySandboxMvp.exe
```

## Application Workflow

1. Enter a valid `http` or `https` target URL.
2. Set the number of sessions.
3. Enter a rotation timeout from `1` to `5` minutes.
4. Import a CSV or use configured provider URLs.
5. Wait for aggregate proxy health progress and the final healthy/dead counts.
6. Press **Start**.
7. Open **Open browser matrix** to view the session tabs.
8. Hide the browser matrix when sessions should continue in the background.
9. Press **Stop** to close session tabs while keeping the main dashboard open.
10. Export the combined log when the run is complete.

Browser audio is muted for every WebView2 session.

## Proxy Management

The proxy manager normalizes endpoint formats into a common model containing host, port, scheme, credentials, provider, latency, health status, and failure count.

HTTP and HTTPS proxies are checked with an HTTP request. SOCKS4 and SOCKS5 proxies are checked with protocol handshakes. Every check has a timeout. A proxy that fails is removed from active assignment and the affected session attempts to acquire a replacement.

Proxy assignment is reserved atomically so concurrent sessions do not intentionally receive the same proxy. Reservations are released when a session stops or rotates.

## CSV Formats

A CSV may contain a single endpoint column:

```csv
proxy
socks5://203.0.113.10:1080
http://203.0.113.11:8080
```

Or separate fields:

```csv
host,port,scheme,username,password
203.0.113.10,1080,socks5,,
203.0.113.11,8080,http,user,secret
```

The importer also recognizes `endpoint`, `address`, `ip`, `protocol`, `user`, and `pass` aliases. Imported rows are normalized, deduplicated, checked, and merged with the current pool.

## Configuration

Configuration is loaded from `config/config.json`:

```json
{
  "SessionCount": 6,
  "RotationMinutes": 5,
  "RotationJitterSeconds": 30,
  "ProxyHealthTimeoutSeconds": 10,
  "ProviderRefreshMinutes": 5,
  "ProviderUrls": [],
  "Headless": false
}
```

The session count is configurable and is not hard-coded to six. The UI limits rotation timeout input to one through five minutes.

## Logging

Logs use JSONL so each event can be processed independently.

Combined application log:

```text
logs/application-YYYY-MM-DD.jsonl
```

Session-specific logs:

```text
logs/session-1-YYYY-MM-DD.jsonl
logs/session-2-YYYY-MM-DD.jsonl
```

Health checks emit aggregate progress events such as `ProxyHealthProgress` and a final `ProxyHealthSummary` rather than writing one warning for every dead proxy. Session events include starts, retries, rotations, failures, and successful views.

Session-specific logs are cleared during normal application shutdown. Combined logs remain available for export.

## Troubleshooting

### The EXE does not open embedded tabs

Install or repair the Microsoft Edge WebView2 Runtime, then launch the EXE again.

### The healthy count is zero

Verify that the CSV contains valid endpoint values, that the proxies are reachable from the current network, and that the source uses supported HTTP, HTTPS, SOCKS4, or SOCKS5 schemes.

### Sessions fail after proxies pass a check

Public proxy lists can become invalid between health checking and browser navigation. The session manager marks the failing proxy, records the error, and attempts replacement up to ten times per session.

### The browser matrix is hidden

Use **Open browser matrix** in the main dashboard. Hiding the matrix does not stop sessions. Use **Stop** to end them.

## Repository Layout

```text
ProxySandboxMvp.csproj
App.xaml
MainWindow.xaml
BrowserWindow.xaml
Models/
Providers/
Services/
config/config.json
logs/
```

## Authorized Use

Use only with systems, URLs, and proxy providers that you own or have explicit permission to test. Do not use the application to generate artificial views, disguise automated traffic, or evade traffic detection.
