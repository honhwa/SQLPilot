# Changelog

Each published change updates the source repository and has a matching GitHub Release.

## [0.13.8](https://github.com/tyeety/SQLPilot/releases/tag/v0.13.8) — 2026-10-07

- Check GitHub's latest stable release in the background after startup and every six hours; show an Update indicator on the SqlPilot toolbar when a newer version is available.
- Add Check for updates / Update to the SqlPilot menu, with an English progress window for downloading and opening official setup.
- Validate release versions and official asset URLs, enforce download size/time limits and verify the installer against SHA256SUMS before opening it.
- Reuse a verified cached installer, remove interrupted downloads and retire old inactive copies while preserving running installers.
- Keep SSMS open while setup stages updates; finish installation after saving queries and closing the selected SSMS host normally.
- Add update transport, version selection, integrity, cancellation and cache lifecycle checks.

## [0.13.7](https://github.com/tyeety/SQLPilot/releases/tag/v0.13.7) — 2026-10-07

- Automatically select and preview the first saved query when opening SQL Library; focus the list for keyboard navigation.
- Add Ctrl+Enter to Insert into query, display the shortcut on the button and support Up/Down navigation from the list or search field.
- Persist library order by last successful insertion into the query editor, independently of edits or saves.
- Retain recent ordering while searching; clear stale previews when search has no results and preserve incoming new-query drafts.
- Add persistence and actual dialog-control checks for selection, navigation, shortcuts, filtering and draft preservation.

## [0.13.6](https://github.com/tyeety/SQLPilot/releases/tag/v0.13.6) — 2026-10-07

- Include SQL column types, lengths/precision and nullability in INSERT value comments.
- Populate INSERT values from declared default expressions, retaining functions and expressions for SQL Server to evaluate; use DEFAULT when a bound or hidden definition is unavailable.
- Use NULL only for nullable columns without defaults; provide editable type-based initial values for required columns without defaults.
- Resolve alias types to their underlying SQL type for initial values, preserve declared types in comments, and support spatial/hierarchy types.
- Leave an explicit required-value placeholder for unfamiliar non-null CLR types instead of guessing a NULL value.
- Preserve Tab navigation for values of different lengths and optional identifier quoting.
- Add an isolated LocalDB metadata/execution fixture covering actual defaults, non-null values, alias types, spatial/hierarchy types and excluded generated columns.

## [0.13.5](https://github.com/tyeety/SQLPilot/releases/tag/v0.13.5) — 2026-10-07

- Move Open SSMS into each detected host row; enable it only after that host installs successfully, including background updates.
- Generate an explicit column list and multiline VALUES body when accepting an unfinished INSERT INTO table target.
- Exclude identity, computed, rowversion, hidden and generated columns; use DEFAULT VALUES when no writable columns remain.
- Select editable value placeholders and navigate them with Tab; preserve existing SELECT, VALUES and column-list continuations.

## [0.13.4](https://github.com/tyeety/SQLPilot/releases/tag/v0.13.4) — 2026-10-07

- Dispose installer extraction directories after interactive, headless and self-test runs, including extraction failures.
- Clean abandoned extraction jobs and completed/expired pending setup jobs in normal and Codex-redirected local storage.
- Protect active setup jobs with file leases, reject links and paths outside managed installer folders, and preserve user settings, SQL Library, History and Sessions.
- Retain limited recent rollback backups and installer logs, with a 128 MiB budget and seven-day retention for managed files.
- Completed background worker EXEs are cleaned on a later setup run after Windows releases the executable.
- Remove inactive SQLPilot native bundle extraction caches while preserving loaded/locked copies and other applications.
- Explicitly release compiler metadata so generated/reference DLL files can be deleted immediately.
- Rotate extension diagnostics with a 2 MiB file limit and one bounded previous copy; truncate oversized entries.
- Add storage lifecycle, log rotation and user-data preservation checks.

## [0.13.3](https://github.com/tyeety/SQLPilot/releases/tag/v0.13.3) — 2026-10-06

- Shortened English and Persian READMEs; removed repeated version strings, fixed-version download/build examples and the outdated About screenshot.
- Moved installation troubleshooting and signing details into focused guides.
- Build/self-test/checksum examples read the version from build metadata.
- Added an optional certificate-store signing step with timestamp and signature verification for the final installer. No signing certificate was available during this release.
- Added this changelog, a packaging check for its release entry, and required source updates plus matching releases in the maintainer workflow.

## [0.13.2](https://github.com/tyeety/SQLPilot/releases/tag/v0.13.2) — 2026-10-06

- Stage updates while SSMS is running; apply after that host exits normally.
- Show pending/applying/completed states, cancellation while waiting and per-host logs.
- Add Open SSMS with version selection after successful installation.
- Fix installation-folder column sizing and dispose process handles used during polling.

## [0.13.1](https://github.com/tyeety/SQLPilot/releases/tag/v0.13.1) — 2026-10-06

- Publish cleaned source, shared product identity and reproducible build/test scripts.
- Add author attribution, Telegram/GitHub links in About, documentation and the source-available use license.
- Include dependency license notices and exclude generated/private files from Git.

## Earlier development

The earlier local prototype added schema-aware completion, JOIN relationships, diagnostics/fixes, snippets, Library, history and session recovery. Experimental AI generation was removed before public source publication. Earlier local builds are not represented as public GitHub releases.
