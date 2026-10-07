# Settings

Open **Settings** from the status window or the tray menu. The application reads `config.local.json` next to its executable at startup; a configuration is required before opening the app.

## Connection details

The Connection view shows the active WireGuard profile, the relay server extracted from the verified wstunnel process, and its current TCP peers. Each relay measurement has a timestamp. A changed or missing listener invalidates the previous relay details.

Relay display keeps only the WSS scheme, host, and port. Credentials, URL paths, query parameters, and the original process command line are not displayed or persisted in relay reports. A server argument or an established TCP connection does not prove that TLS or the VPN works.

## Editable configuration

You can edit both profile names, the wstunnel executable path, the local listener port, the router and home addresses, the home subnet, the DNS test server and hostname, the monitoring interval, and the probe timeout.

**Save** validates the draft, atomically replaces `config.local.json`, preserves unknown JSON fields, and starts a fresh measurement. Previous measurements are invalidated. A save failure leaves the active configuration intact and keeps the settings window open.

**Cancel** discards the edited monitoring settings. Language and **Start at Windows sign-in** have their own immediate behavior and remain applied even if you subsequently cancel.

Addresses must be IPv4. The home subnet prefix must be between 8 and 30. The listener port must be between 1 and 65535, the interval between 5 and 300 seconds, and the timeout between 250 and 3000 milliseconds. Profile names must be distinct, and the executable path must be absolute after expanding environment variables.

## Control scope

Monitoring settings do not modify WireGuard profile contents, private keys, wstunnel launch scripts, or the relay destination. Use the existing client tools to manage those components; **Open WireGuard** opens the profile manager.

The network-control helper supports the default `TunnelWatchWss` client layout described in the [README](../README.md). Changing the profile names, listener port, or executable path disables those controls rather than operating another client's profiles.

## Local data

The configuration is intentionally local and ignored by Git. Exported observations and reports can contain IP addresses, adapter or profile names, process IDs, relay hosts, TCP peers, and runtime errors. Store them locally and review them before sharing.
