# Validation

The [GitHub Actions workflow](../.github/workflows/validate.yml) runs core/workspace tests and formatting checks. Release-specific changes and validation notes are recorded in [Changelog](../CHANGELOG.md) and [GitHub Releases](https://github.com/tyeety/SQLPilot/releases).

Local host builds compile adapters against installed SSMS editor assemblies. The packaged installer's `--self-test` exercises adapter compilation, version/cleanup safeguards, pending-setup state transitions and layout without modifying installed hosts. Reports are generated under ignored `artifacts/`.

Compilation and offscreen layout checks do not replace interactive acceptance: verify loading, keyboard completion, connection switching, formatting, sessions, background installation and launching in the selected host. Record untested behavior in each release's notes.

No user SQL is executed by the validation scripts. Optional LocalDB metadata tests require an explicitly named dedicated test instance; see [building](BUILDING.md).
