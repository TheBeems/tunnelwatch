# Languages

TunnelWatch defaults to English, independently of the Windows display language.
An existing configuration without a `Language` field also uses English.
Choose **Settings → Language → English / Nederlands** in the system tray, or
open the gear button in the status window. The interface switches immediately;
the existing VPN is not changed. The chosen language has a checkmark and is
remembered per Windows user in `%LOCALAPPDATA%\TunnelWatch\language.txt`.
No UAC or write access to the application folder is needed.

The saved user preference takes precedence over `Language` in `config.local.json`.
If no preference exists, the configuration value applies; if neither exists,
English applies. A damaged or unreadable preference falls back to the configuration.
The app writes the small preference file atomically and reports a save failure
before changing the interface. Personal network configuration is not rewritten.

## Add a language

1. Copy `resources/Strings.resx` to, for example, `resources/Strings.de.resx`.
2. Translate the `<value>` text. Keep each `name` and numbered placeholders such
   as `{0}` and `{1}` intact. Translate whole messages, rather than assembling
   fragments in code. Product names and action identifiers remain unchanged.
3. Run `./build.ps1`. It discovers all `Strings.<culture>.resx` files automatically,
   validates keys and formatting placeholders, and compiles string-only resources
   into the output's `locales` folder. No NuGet, new library, or language registry
   in C# is needed.
4. Open **Settings → Language** and select the new language. Compiled language
   files are discovered automatically and shown using their native language names.
   Inspect the actual interface for text wrapping, tooltips and keyboard access.

Missing keys fall back to the parent language and then the English base file:
`nl-NL` → `nl` → English. A valid but untranslated language also falls back to
English. Invalid culture codes produce a configuration error. Partial translations
are permitted; an isolated partial French fixture verifies per-key fallback in the
tests. The shipped Dutch translation has all English keys.

## Implementation and packaging

- `resources/Strings.resx`: complete English source and stable message keys.
- `resources/Strings.nl.resx`: Dutch translation.
- `src/L.cs`: the shared .NET `ResourceManager` accessor.
- `src/LanguagePreferences.cs`: the separate per-user language preference.
- `scripts/Build-Resources.ps1`: compilation with the installed .NET
  `ResourceWriter`; only string values are accepted.
- The app and its PowerShell control helper read the same compiled language files.
  Ship the entire `locales` folder together with the executable and helper.

This small portable build uses standalone `.resources` files, a supported desktop
`ResourceManager` deployment option. A Visual Studio/MSBuild project commonly
packages the same RESX source in satellite assemblies instead. The English file is
`locales/Strings.resources`; Dutch is `locales/Strings.nl.resources`. Resource files
are application assets, not executable configuration or user-uploaded translations.

The app changes `CurrentUICulture` and uses an explicit language for resource
lookups, so queued callbacks cannot restore old-language labels. In-flight status
measurements are invalidated and replaced with a fresh measurement. Language
selection is disabled during a VPN control action. The status window keeps its
position and expanded Details state when switching. The app preserves
`CurrentCulture` for the user's date and number formatting. Menus, status text,
tooltips, accessibility labels, diagnostics and app-owned helper results are
localized. Windows error messages in diagnostics are looked up explicitly in the
displayed app language, with English fallback if Windows has no matching message
pack. Error codes are retained; unknown codes use a translated numeric message.
This also covers socket errors and Windows HRESULTs in IO/permission exceptions.
Interface/profile names and messages from the external VPN helper retain their
original content.

New languages need native layout review. Right-to-left layout and languages with
different plural rules may need additional UI or message design; no such layouts
have been verified yet.

Reference: [Microsoft ResourceManager documentation](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-resources-resourcemanager).
Windows errors: [Microsoft FormatMessageW documentation](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-formatmessagew).
