# Maintainer release checklist

Only Arash Ghasemi Rad or a maintainer with his written permission may publish releases.

1. Update `Directory.Build.props` and add a dated entry to `CHANGELOG.md` for every published change. Keep version numbers out of the README; versions belong in build metadata, changelog and release tags.
2. Run `scripts/Test.ps1`, format verification, host builds and installer packaging. Commit and push the validated source, then publish an accompanying release.
3. Run the packaged EXE's `--self-test`; check the report's version and `installedByTest=false`.
4. After saving/closing SSMS, verify loading and keyboard behavior in each supported host. Record any unverified scope honestly.
5. Check source for credentials, user SQL, local paths, generated output and obsolete files. Keep third-party notices in the package.
6. Tag the tested commit and attach the installer and `SHA256SUMS.txt` to GitHub Releases. Do not commit installers to the source tree.

Typical checksum command:

```powershell
[xml]$props = Get-Content Directory.Build.props
Get-FileHash ("artifacts/release/SqlPilotSetup-{0}.exe" -f $props.Project.PropertyGroup.Version) -Algorithm SHA256
```

GitHub Actions validates core/workspace behavior and formatting. It does not have installed SSMS host APIs and cannot substitute for host builds or interactive acceptance.

Sign before calculating checksums and uploading assets. Use the matching changelog entry as the release notes. Never replace an already published version's installer with a different binary. See [signing](SIGNING.md).
