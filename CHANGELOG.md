# Changelog

Each published change updates the source repository and has a matching GitHub Release.

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
