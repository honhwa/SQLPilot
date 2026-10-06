# Validation for 0.13.2

Recorded on 2026-10-06, from the cleaned source tree.

| Check | Result |
| --- | --- |
| Core completion, syntax, snippets, fixes and context fixtures | 229 checks passed |
| Windows workspace fixtures: DPAPI sessions, shortcuts, navigation and About | 30 checks passed |
| SSMS 20 Release adapter build | Passed; zero warnings/errors |
| SSMS 22 Release adapter build | Passed; zero warnings/errors |
| Packaged installer self-test | 38 checks passed; `installedByTest=false` |
| Deferred setup waiting, cancellation, timeout and atomic status fixtures | 8 checks passed (included above) |
| Installer completion action visibility and WPF layout | 2 checks passed (included above) |
| C# format verification | Passed |
| Retired AI source/assets in installer and VSIX | Absent |
| License and dependency notice packaging | Present |

The installer self-test detects installed SSMS versions, compiles adapters against their APIs, and checks version/cleanup safeguards without installing over them. Reports and build output are generated under ignored `artifacts/`; personal installation paths and logs are not published.

About content is tested for the author, Telegram, repository and license. Its WPF content was rendered offscreen and visually inspected; [preview](images/about.png). This is a layout check, not a live SSMS screenshot or a browser-opening test.

No user SQL was edited or executed during this cleanup. Real SSMS keyboard, connection switching and session-reopening acceptance must be repeated after installing and restarting a selected host. The new installer was not installed over the user's running SSMS. Historical prototype testing is not a blanket compatibility guarantee for this release.

GitHub Actions separately runs core/workspace tests and formatting on a Windows runner. Its result should be checked in the repository; local success does not imply that the hosted workflow has already completed.

The completion installer screen was rendered offscreen and visually inspected. Deferred setup is tested with controlled running/cancellation callbacks; no actual installation was queued on the user's hosts, no SSMS process was closed and the Open SSMS launch action was not invoked. Real elevated background installation and shell launch still require acceptance on a selected host. Pending jobs continue after closing setup, but do not survive a Windows reboot.
