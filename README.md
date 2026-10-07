# TunnelWatch

TunnelWatch is a small Windows system tray application that monitors an existing WireGuard connection carried over a secure WebSocket (WSS) transport. It gives you a compact view of the transport, VPN mode, and access to your home network.

The app uses Windows Forms and .NET Framework 4.8.1. It builds with the compiler installed on Windows, without NuGet packages, Visual Studio, WSL, or a web service.

## What it does

- Checks the WireGuard services, the local WSS listener and its owning executable, the route selected by Windows, and router reachability.
- Distinguishes split tunnel, full tunnel, tunnel off, direct home access, errors, and uncertain measurements.
- Shows connection details, the active WireGuard profile, and relay and TCP peer information from the verified WSS process.
- Provides editable monitoring settings, an optional DNS check, English and Dutch interfaces, and optional startup at Windows sign-in.
- Offers explicit WSS and VPN controls for the supported client installation. These controls request administrator permission; normal monitoring runs as a standard user.

Monitoring does not automatically reconnect, switch profiles, or change routes. A TCP connection alone is not treated as proof of a working VPN. TunnelWatch does not install WireGuard, wstunnel, a relay server, or WireGuard profiles, and it does not read WireGuard private keys.

## Requirements

- Windows 11 with .NET Framework 4.8.1 and its C# compiler installed. The build script checks the Framework version and looks for the ARM64 or 64-bit compiler.
- Windows PowerShell 5.1 for the build scripts and optional control helper.
- An existing WireGuard and wstunnel client setup for useful connection monitoring.
- A writable application folder for saving connection settings.

A .NET SDK is only needed for the optional portable model tests. The application is compiled as `AnyCPU`.

## Build

Open PowerShell in the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The default output is `bin`. To build and run the synthetic self-tests in a separate output directory:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test -OutputDirectory 'artifacts\build'
```

No package restore or download is performed. `-ExecutionPolicy Bypass` applies only to this PowerShell process and does not change the machine's execution policy. Use a build method permitted by your Windows policy. Compiled outputs and test reports are ignored by Git.

The build produces:

```text
bin/
  TunnelWatch.exe
  TunnelWatch.exe.config
  Control-TunnelWatch.ps1
  locales/
    Strings.resources
    Strings.nl.resources
```

When a root `config.local.json` already exists, the build also copies it into the output folder. Treat that folder as private once it contains your configuration.

## Configure

For the default build, create the configuration next to the executable:

```powershell
Copy-Item .\config.example.json .\bin\config.local.json
notepad.exe .\bin\config.local.json
```

For a custom output directory, copy the example into that directory instead. The example uses fictional documentation addresses and a reserved `.test` hostname. Replace them with your own values before using the application.

| Setting | Purpose |
| --- | --- |
| `Language` | Interface language, initially `en`; `nl` is also included. |
| `SplitProfile`, `FullProfile` | WireGuard profile names, without the `WireGuardTunnel$` service prefix. |
| `WssExecutable` | Full path to the expected wstunnel executable; environment variables are supported. |
| `ListenerPort` | Local UDP listener port owned by wstunnel. |
| `Router` | Router IPv4 address used to verify the VPN route and reachability. |
| `HomeHost`, `HomeNetwork` | Home IPv4 target and subnet used to identify direct home access. |
| `DnsServer`, `DnsName` | IPv4 DNS server and hostname for an explicitly requested DNS check. |
| `IntervalSeconds` | Monitoring interval, from 5 to 300 seconds. |
| `TimeoutMilliseconds` | Probe timeout, from 250 to 3000 milliseconds. |

Settings can also be edited in **Settings** after the application starts. **Save** validates and persists monitoring settings, then refreshes the measurements. Language and startup preferences apply immediately. See [Settings](docs/SETTINGS.md) and [Languages](docs/LOCALIZATION.md).

## Install and run

TunnelWatch is portable; no installer or MSIX registration is required.

1. Build the application.
2. Copy the executable, its `.config` file, `Control-TunnelWatch.ps1`, the entire `locales` folder, and your `config.local.json` into a permanent, writable folder.
3. Run `TunnelWatch.exe` as your normal Windows user. Add `--show` to open the status window immediately.
4. Optionally enable **Start at Windows sign-in** in Settings or the tray menu.

For the default build:

```powershell
.\bin\TunnelWatch.exe --show
```

Click the tray icon to show status, or right-click it for refresh, settings, WireGuard, and exit actions. Closing the status window hides it; **Exit** stops TunnelWatch and leaves the existing VPN running. To remove the portable installation, disable startup, exit the app, and remove its application folder. The language preference is stored separately in `%LOCALAPPDATA%\TunnelWatch\language.txt`.

The repository does not include a signing certificate or signed binaries. Whether a local build can run depends on your Windows application-control policy.

## Optional connection controls

General monitoring supports the profile names, executable path, and port in your configuration. The built-in network controls use the following default WSS client layout:

- Profiles: `tunnelwatch-wss-split` and `tunnelwatch-wss-full`.
- wstunnel: `%LOCALAPPDATA%\TunnelWatchWss\wstunnel.exe` on local UDP port `39075`.
- Transport scheduled task: `TunnelWatch-WSS-WireGuard-transport`.
- Existing mode helper: `%LOCALAPPDATA%\TunnelWatchWss\TunnelWatch-Wss-Full.ps1`.

These names describe the helper contract; the client setup, private profiles, and external mode helper are not bundled. Custom monitoring profiles, executable paths, or ports disable these controls. Changing VPN mode or stopping WSS can interrupt connectivity, so use the controls only with a correctly configured client and a recovery plan.

## Tests and diagnostics

Run the application's synthetic tests with `build.ps1 -Test`. They cover connection state, localization, settings validation, relay-address redaction, and settings persistence. They do not switch the live VPN.

With an installed .NET SDK, the portable subset can also be run without NuGet or restore:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\test-model.ps1
```

For a one-time observation using your configured network:

```powershell
.\bin\TunnelWatch.exe --observe --report .\observation.local.json
# Optional: add --dns for a DNS probe, or --relay for relay details.
```

Diagnostic reports contain local network information and must stay private. The separate `tests/Test-ControlLive.ps1` script changes the live VPN; it is an explicit manual integration test, not part of the normal build or self-tests.

## Repository contents and privacy

The repository contains source code, translation resources, build and test scripts, documentation, and a fictional configuration example. Build outputs, local configuration, credentials, screenshots, reports, and workstation-specific historical files are excluded by `.gitignore`.

Tests intentionally use fictional hosts, documentation IP addresses, and dummy credential strings to verify that relay details omit credentials, paths, and query parameters. These are not usable connection credentials. See [Privacy and publication](docs/PRIVACY.md).
