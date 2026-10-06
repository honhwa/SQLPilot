# Maintainer release checklist

Only Arash Ghasemi Rad or a maintainer with his written permission may publish releases.

1. Update `Directory.Build.props` and the README download/build examples for the release.
2. Run `scripts/Test.ps1`, format verification, host builds and installer packaging.
3. Run the packaged EXE's `--self-test`; check the report's version and `installedByTest=false`.
4. After saving/closing SSMS, verify loading and keyboard behavior in each supported host. Record any unverified scope honestly.
5. Check source for credentials, user SQL, local paths, generated output and obsolete files. Keep third-party notices in the package.
6. Tag the tested commit and attach the installer and `SHA256SUMS.txt` to GitHub Releases. Do not commit installers to the source tree.

Typical checksum command:

```powershell
Get-FileHash artifacts/release/SqlPilotSetup-0.13.2.exe -Algorithm SHA256
```

GitHub Actions validates core/workspace behavior and formatting. It does not have installed SSMS host APIs and cannot substitute for host builds or interactive acceptance.
