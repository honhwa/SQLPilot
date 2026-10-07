# Installation details

Download the installer from [GitHub Releases](https://github.com/tyeety/SQLPilot/releases/latest). Select your SSMS installations and grant administrator access when Windows requests it.

If SSMS is running, setup prepares the update and waits for that version's processes to exit. Save queries, close SSMS normally, and wait for completion before reopening. Setup never closes SSMS itself. **Open SSMS** is available in each version's row and becomes enabled after that version installs successfully.

Pending setup continues after closing the installer. **Cancel pending** cancels jobs still waiting, not an installation already applying files. Keep Windows running until completion. If Windows restarts or a pending job expires, run setup again. Results are stored in `%LOCALAPPDATA%\SqlPilot\PendingSetup\<job>\status.json` and setup logs under `%LOCALAPPDATA%\SqlPilot\SetupLogs`.

Setup reports existing copies and requests confirmation for a downgrade. Backups are saved in the setup logs folder.

## Windows publisher warning

Check the asset's signing status in its release notes and compare the installer with `SHA256SUMS.txt`. Download only from official releases. Signing prerequisites and Microsoft Store constraints are documented in [SIGNING.md](SIGNING.md).

## Installer disk usage

Extraction files are removed when setup closes or a headless/self-test run ends. Later runs remove abandoned jobs and retired background worker copies from both normal local storage and Codex-redirected storage. Active jobs are protected by file leases. Managed rollback backups and logs have a 128 MiB budget and seven-day retention; unknown files are left intact. Settings, SQL Library, History and Sessions are preserved. Windows may keep a running worker EXE locked until exit; a later setup run removes its completed job.

Extension diagnostics rotate at 2 MiB and keep one previous bounded copy. Existing oversized diagnostic logs are discarded on the next diagnostic write. User SQL files and encrypted sessions are excluded from these policies.

Setup also removes inactive SQLPilot native bundle caches under the temporary `.net` folder. Loaded or locked copies and caches belonging to other applications are preserved.
