# Release process

## Versioning

Use four-part numeric MSIX versions such as `0.2.1.0`. Create a matching Git tag such as `v0.2.1`. The release workflow converts a three-part tag to its four-part MSIX version.

## Local package

On Windows with .NET 10, the Windows SDK, and Inno Setup 6:

```powershell
.\scripts\Build-Release.ps1 -Version 0.2.1.0
```

Without signing parameters, the script creates a temporary two-year development certificate, signs both architecture packages and `AchievementRelay_Setup.exe`, exports only the public certificate, and deletes the private key file from its temporary folder. The versioned ZIP remains as a manual fallback.

For a production certificate whose subject matches `CN=Achievement Relay Open Source`:

```powershell
.\scripts\Build-Release.ps1 `
  -Version 0.2.1.0 `
  -PfxPath C:\secure\AchievementRelay.pfx `
  -PfxPassword $env:ACHIEVEMENT_RELAY_PFX_PASSWORD
```

## GitHub signing secrets

The tag workflow supports these repository secrets:

- `SIGNING_PFX_BASE64`: Base64 encoding of the PFX bytes
- `SIGNING_PFX_PASSWORD`: PFX password
- `SIGNING_TIMESTAMP_URL`: RFC 3161 timestamp service URL for production signatures

If either is absent, the workflow makes a development-signed alpha release and includes `AchievementRelay.Development.cer`. Never commit a PFX, password, or Base64 private key.

## Checklist

1. Update application/file versions and release notes.
2. Run the core contract checks and repository checks.
3. Build and install both target packages on representative Windows devices where available.
4. Verify installer connect-now/skip paths, encrypted handoff deletion, desktop-shortcut choice, first baseline, Discord test, real Xbox account sync, tray close/reopen, startup, upgrade, and uninstall.
5. Confirm the package certificate subject exactly matches the manifest publisher.
6. Tag the verified commit with `v<major>.<minor>.<patch>` and push the tag, or run the **Release** workflow from GitHub Actions and enter that version. The manual workflow creates the tag at the selected commit when it publishes the release.
7. Confirm `AchievementRelay_Setup.exe`, both MSIX packages, the manual ZIP, and the public `.cer` for development-signed builds are attached to the release.
8. Download the published release assets and verify signatures with `Get-AuthenticodeSignature` or `SignTool verify /pa`.

Store onboarding may replace the local package identity and publisher. Treat those values as permanent after the first stable public distribution.
