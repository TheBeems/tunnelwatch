# Privacy and publication

TunnelWatch's public source should contain only code, resources, build and test scripts, documentation, and fictional examples.

## Keep local

- `config.local.json` and any other `*.local.*` files, including screenshots and reports.
- WireGuard profiles, passwords, access tokens, private keys, signing certificates, and environment files.
- Build outputs, packages, backups, and review artifacts.
- Diagnostics containing real addresses, hostnames, profile names, process information, or Windows user paths.

`.gitignore` excludes these common locations and file types. It also excludes the original workstation's historical reports and installation helpers that depend on a particular reviewed binary. Ignoring a file does not remove it from disk and does not erase a file already committed to Git.

## Public examples

`config.example.json` and synthetic tests use documentation addresses in `192.0.2.0/24`, `198.51.100.0/24`, and `203.0.113.0/24`, plus reserved `.test` hostnames. The IPv6 relay example uses `2001:db8::/32`. Loopback and unspecified addresses are used for local socket behavior. None of these examples identifies a private deployment.

Some tests include dummy usernames, passwords, or query tokens to check relay-address redaction. They are deliberately fictional test inputs. The default task, helper, and profile names describe the supported WSS client integration; they contain no passwords or relay endpoints.

The app does not read WireGuard private keys or profile files. It performs local monitoring and configured network probes, and reports stay at the file path you select. Local configuration and diagnostic reports still need review before you share them.

## Before pushing

Review `git status`, `git diff --cached`, and `git ls-files`, and run a local secret scan on the exact files and commit history being uploaded. Do not force-add ignored configuration, diagnostics, or binaries. A clean automated scan should be combined with manual review of connection values and personal information.
