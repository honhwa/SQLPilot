# Signing and Microsoft Store

## GitHub installer signing

`Build-Setup.ps1` can sign the final EXE through Windows SDK SignTool using a code-signing certificate in the Windows certificate store. It verifies trust, the publisher thumbprint and the timestamp before copying the final release asset. A requested signing failure stops packaging; it does not silently publish an unsigned installer.

```powershell
pwsh -File scripts/Build-Setup.ps1 -SigningThumbprint $env:SQLPILOT_SIGNING_THUMBPRINT -SignToolPath $env:SQLPILOT_SIGNTOOL
```

The optional `-SigningLocalMachine` selects the machine certificate store. Private keys stay in the certificate provider/token; do not place keys or passwords in Git or chat. If no signing thumbprint is supplied, packaging does not sign. Signing status belongs in each release's notes. Generate checksums after signing.

A certificate must have code-signing usage, an accessible private key and a chain trusted by Windows. Self-signed certificates are for development, not public publisher trust. [Microsoft signing guidance](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options).

This step signs the final installer only. It does not claim that every embedded/generated PE file is signed or that the package meets Store certification requirements.

## Microsoft Store readiness

For the EXE route, Microsoft requires a signed installer and PE files, a fixed versioned HTTPS download URL and silent installation. See [official package requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msi/app-package-requirements).

SqlPilot currently generates host adapters during installation. Store preparation must address those generated binaries and the full dependency payload, test silent installation/uninstallation and updates, disclose the SSMS dependency, and complete certification through an authorized Partner Center account. The existing headless mode is not evidence of Store readiness.

Microsoft signs certified MSIX submissions, but converting this installer to MSIX is a separate packaging/integration task: installing extensions into an external SSMS host must be redesigned and validated. A cosmetic format conversion is insufficient. [Microsoft distribution options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options).

No Store listing or trusted signing identity is included in this source repository. Publishing needs the author's developer account and signing setup; do not create a substitute publisher identity.
