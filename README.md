# Proxy Sandbox

A .NET 8 WPF desktop QA sandbox for authorized testing with isolated proxy-backed WebView2 sessions. Sessions run in separate browser profiles, use independent proxies, rotate automatically, and expose their status in the dashboard.

## Run The Source

1. Install the .NET 8 SDK on Windows.
2. Install the Microsoft Edge WebView2 Runtime.
3. From this directory run `dotnet restore` and `dotnet run`.

## Run The EXE

The self-contained Windows build is located at:

`bin/Release/net8.0-windows/win-x64/publish/ProxySandboxMvp.exe`

Double-click the executable to launch it. The WebView2 Runtime is still required on the machine.

## Workflow

1. Enter an authorized target URL.
2. Set the session count.
3. Enter a rotation timeout from `1` to `5` minutes.
4. Import a CSV or configure provider URLs.
5. Wait for the `Healthy`, `Dead`, and `Pool` counts.
6. Press **Start**.
7. Use **Open browser matrix** to view the muted embedded sessions.
8. Press **Stop** to stop sessions while keeping the main dashboard open.
9. Use **Export logs** to copy the combined log to Downloads.

The dashboard reports successful views, active sessions, failed sessions, latency, proxy assignment, and errors. Failed proxies are replaced automatically when possible.

## Proxy Inputs

CSV files support a `proxy`, `endpoint`, or `address` column, including values such as `http://user:password@host:port`, `socks4://host:port`, and `socks5://host:port`. Separate `host`, `port`, `scheme`, `username`, and `password` columns are also supported. CSV imports are normalized, deduplicated, protocol-checked, and merged into the active pool.

Provider URLs may return a JSON array, `{ "proxies": [] }`, or comma/newline-separated endpoint values. Health checks report aggregate progress and do not emit one warning per dead proxy.

## Logs

Combined application logs are stored as daily JSONL files under `logs/`:

`logs/application-YYYY-MM-DD.jsonl`

Session-specific events are written to:

`logs/session-<id>-YYYY-MM-DD.jsonl`

Session log files are removed when the application closes. Exported combined logs are copied to the Windows Downloads folder with a timestamped filename.

## Documentation

See [DOCUMENTATION.md](DOCUMENTATION.md) for architecture, configuration, troubleshooting, and release instructions.

Only use this application with systems, URLs, and proxy providers you are authorized to test.
