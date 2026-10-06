# Changelog

Each published change updates the source repository and has a matching GitHub Release.

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
