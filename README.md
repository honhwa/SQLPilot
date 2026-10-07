<p align="center"><img src="assets/sqlpilot.svg" width="88" alt="SqlPilot icon"></p>

# SqlPilot

A keyboard-first SQL development assistant for **SQL Server Management Studio (SSMS)**.

Created by **Arash Ghasemi Rad** · [Telegram: @ArashGhasemiRad](https://t.me/ArashGhasemiRad)

[Changelog](CHANGELOG.md) · [Download releases](https://github.com/tyeety/SQLPilot/releases) · [فارسی](README.fa.md) · [License](LICENSE)

SqlPilot provides local, database-aware completion, editable snippets, SQL diagnostics and a searchable query library. The interface and installer are in English.

## Install

1. Download the installer from the official [Releases](https://github.com/tyeety/SQLPilot/releases) page.
2. Save your queries. You can leave SSMS open while setup prepares the update.
3. Run the installer, select the detected SSMS versions and click **Install selected components**. Administrator access is required for host installation.
4. If SSMS is running, setup shows **Prepared · restart required**. Close that version normally; background setup applies the update after all its processes exit. Wait for **Installed · ready to open**, then click **Open SSMS** in that version's row. Open a connected SQL query tab and use the **SqlPilot** toolbar button.

Keep Windows running until pending installation finishes. See [installation details](docs/INSTALLATION.md) for background updates and troubleshooting.

## Compatibility

Setup detects local SSMS installations and builds an adapter against their editor APIs. Compatibility depends on the host and database metadata permissions; see [validation](docs/VALIDATION.md).

## Features

- Contextual tables, views, columns, procedures, functions and SQL keyword suggestions; flexible initials/substring matching, subtle matched-character highlighting and schema provenance.
- Alias-aware completion and procedure parameter placeholders. Tab and Shift+Tab move through inserted parameters.
- Accept a table after `INSERT INTO` to generate writable columns and a multiline `VALUES` template with column types in comments. Declared defaults take priority; nullable columns use `NULL`, and required columns use editable type-based initial values. Tab moves between values; existing INSERT continuations are preserved.
- JOIN targets ranked by declared foreign keys; ON conditions include composite and reverse relationships. Relations are never guessed solely from column names.
- **Tab on a SELECT projection `*`** expands visible columns vertically. A small editor hint explains the action when expansion is available.
- Editable Tab snippets, such as `ssf` → `SELECT * FROM`, and configurable keyboard shortcuts.
- Local syntax errors, warnings and improvement suggestions with per-section underlines, explanations and safe fixes where available.
- Formatting that preserves comments and optional conversion of typed `&&`, `||`, `==`, `!=` in SQL conditions.
- SQL Library with categories, tags, search and import/export. The last inserted query appears first and is selected on opening; Up/Down selects a query and Ctrl+Enter inserts it. Query history, reference navigation and Object Explorer lookup are also available.
- Encrypted tab/session checkpoints and connection recovery for supported connection types.

The toolbar uses one **SqlPilot** button with grouped actions. Completion, appearance, bracket insertion, aliases, diagnostics and session recovery are configurable in Settings.

## Default keys

| Key | Action |
| --- | --- |
| Ctrl+Space | Explicit completion, including revisited or mistyped identifiers |
| Tab / Enter | Accept a suggestion |
| Up / Down | Select a suggestion |
| Esc | Dismiss; blank lines stay quiet until typing or explicit completion |
| Tab at a projection star | Expand available columns |
| Alt+Enter or Ctrl+. | Diagnostic explanation and available fixes |
| Ctrl+Shift+F | Format selection/document |
| Ctrl+Shift+A | Analyze and fix |
| Ctrl+Shift+R | Refresh schema |
| Ctrl+Shift+C | Metadata connection |
| Ctrl+Shift+H | Query history |
| Ctrl+Shift+L | SQL Library |
| Ctrl+Shift+S | Save SQL to Library |
| F12 | Go to reference; procedures open as ALTER scripts |
| Ctrl+F12 | Locate an object in the connected Object Explorer |

Bindings can conflict with other SSMS extensions. Edit them under **SqlPilot → Keyboard shortcuts**. SqlPilot can suppress other completion providers while its completion is enabled and restore their prior state afterward.

## Data and behavior

Completion and diagnostics run locally; there is no AI service or telemetry integration. Schema refresh reads SQL Server metadata. Completion, fixes and reference scripts change editor text only; they do not execute your SQL.

Settings, snippets, Library, history and sessions are stored under `%LOCALAPPDATA%\SqlPilot`. Session text and remembered connection details are protected with Windows DPAPI for the current account. History and Library are local files and can contain sensitive SQL: protect the Windows account and filesystem. Entra/token renewal and some advanced/cross-database SQL scopes are not fully supported.

## Build and test

Use Windows, the SDK selected by `global.json`, PowerShell and an installed SSMS host.

```powershell
pwsh -File scripts/Test.ps1
pwsh -File scripts/Build.ps1
pwsh -File scripts/Build-Setup.ps1
```

Build outputs are in `artifacts/`. See [building](docs/BUILDING.md) and [release workflow](docs/RELEASING.md).

## Source layout

| Location | Purpose |
| --- | --- |
| `src/SqlPilot.Core` | Completion, T-SQL parsing, formatting, diagnostics and snippets |
| `src/SqlPilot.Ssms` | Editor integration, metadata access, Library, sessions and navigation |
| `src/SqlPilot.Setup` | SSMS detection, selected-host installer, backups and version checks |
| `src/Shared` | Shared design and author/product identity |
| `tests` | Core, workspace and opt-in LocalDB checks |
| `scripts` | Build, installer packaging and test entry points |
| `assets`, `docs`, `examples` | Original artwork, documentation and keyboard examples |
| `third-party/licenses` | Dependency licenses and notices |

## License and author

Copyright © 2026 **Arash Ghasemi Rad**. All rights reserved.

This is **source-available**, not OSI open source. [LICENSE](LICENSE) allows free installation and use of the original version, including internal business use and copies needed to install/build it. Modification, code reuse, redistribution and resale require the author's written permission. Preferences, your own snippets and your own SQL remain yours.

Public GitHub hosting permits viewing and platform forks under [GitHub's terms](https://docs.github.com/en/site-policy/github-terms/github-terms-of-service#5-license-grant-to-other-users); a license cannot technically prevent copying. Independent dependencies retain their own [licenses](THIRD_PARTY_NOTICES.md).

Report bugs through [Issues](https://github.com/tyeety/SQLPilot/issues), without passwords, connection strings or confidential query/data content. For permissions or author contact: [@ArashGhasemiRad](https://t.me/ArashGhasemiRad). See [contribution policy](CONTRIBUTING.md).
