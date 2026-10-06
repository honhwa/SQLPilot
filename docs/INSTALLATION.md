# Installation details

Download the installer from [GitHub Releases](https://github.com/tyeety/SQLPilot/releases/latest). Select your SSMS installations and grant administrator access when Windows requests it.

If SSMS is running, setup prepares the update and waits for that version's processes to exit. Save queries, close SSMS normally, and wait for completion before reopening. Setup never closes SSMS itself. Use **Open SSMS** after installation.

Pending setup continues after closing the installer. **Cancel pending** cancels jobs still waiting, not an installation already applying files. Keep Windows running until completion. If Windows restarts or a pending job expires, run setup again. Results are stored in `%LOCALAPPDATA%\SqlPilot\PendingSetup\<job>\status.json` and setup logs under `%LOCALAPPDATA%\SqlPilot\SetupLogs`.

Setup reports existing copies and requests confirmation for a downgrade. Backups are saved in the setup logs folder.

## Windows publisher warning

Check the asset's signing status in its release notes and compare the installer with `SHA256SUMS.txt`. Download only from official releases. Signing prerequisites and Microsoft Store constraints are documented in [SIGNING.md](SIGNING.md).
