# Release process

## Versioning

Use four-part numeric MSIX versions such as `0.4.0.0`. Create a matching Git tag such as `v0.4.0`. The release workflow converts a three-part tag to its four-part MSIX version.

v0.4.0 pull-request artifacts use the reserved pre-release package lane `0.3.99.<run>` while reporting product version `0.4.0`. Each test build upgrades over earlier pre-official packages, while the public `0.4.0.0` package remains numerically newer than every test build. Do not move v0.4.0 test artifacts into a `0.4.0.<run>` package lane; Windows would then treat the final `0.4.0.0` release as a downgrade.

## Local package

On Windows with .NET 10, the Windows SDK, and Inno Setup 6:

```powershell
.\scripts\Build-Release.ps1 -Version 0.4.0.0
```

Without signing parameters, the script creates a temporary two-year development certificate, signs both architecture packages and `AchievementRelay_Setup.exe`, exports only the public certificate, and deletes the private key file from its temporary folder. The versioned ZIP remains as a manual fallback. This path is for local/CI testing only: each generated certificate has a different fingerprint, so one test build must not silently trust a later test installer.

For the reviewed persistent project certificate whose subject matches `CN=Achievement Relay Open Source`, first trust its public half in **Local Computer → Trusted People** on the build machine, then run from an elevated PowerShell session:

```powershell
.\scripts\Build-Release.ps1 `
  -Version 0.4.0.0 `
  -PfxPath C:\secure\AchievementRelay.pfx `
  -PfxPassword $env:ACHIEVEMENT_RELAY_PFX_PASSWORD `
  -TimestampUrl http://timestamp.digicert.com `
  -AllowUntrustedProjectCertificate
```

That switch does not accept an arbitrary self-signed key. The build hashes the PFX leaf and requires an exact match with `release/AchievementRelay.Publisher.cer`, whose public details are recorded in `release/publisher-certificate.json`. The public certificate is added to Setup, the fallback ZIP, and the release assets. The protected PFX and password must never enter the repository or an artifact.

## Automatic-update release contract

Every build creates `AchievementRelay_Update.json` and `AchievementRelay_Update.sig` beside the installer. The manifest contains the three-part product version, four-part Windows package version, reviewed minimum supported version, UTC build timestamp, exact installer name, byte size, and SHA-256. The `.sig` envelope contains the release certificate and an RSA/SHA-256 signature over the exact manifest bytes. The app pins that certificate and also requires the signed values to agree with GitHub's latest stable release tag, asset metadata, and the setup executable's product/file versions. The separate package version lets final `0.4.0.0` correctly supersede a `0.3.99.<run>` test package even though both report product version `0.4.0`.

`release/update-policy.json` is authoritative for `minimumSupportedVersion`. Leave it at the oldest still-supported updater-capable version for an optional release. Raise it only through a reviewed commit when older builds must stop monitoring and update. The minimum cannot exceed the release being built. Never edit or replace the manifest or installer inside an existing release; publish a higher patch version.

The package build hashes the leaf signing certificate and embeds that SHA-256 fingerprint into the app. A downloaded installer must pass Windows Authenticode verification and match that pin. Certificate rotation therefore needs a transition build, signed by the old certificate, with the replacement fingerprint temporarily added to `additionalPublisherCertificateSha256` in the reviewed policy. Only after that transition is widely installed may a later release change signer; remove the old fingerprint in a subsequent release.

The deliberately isolated baseline-to-target exercise is documented in [`LIVE-UPDATE-TEST.md`](LIVE-UPDATE-TEST.md). Its workflow uses a separate reviewed support policy and one ephemeral certificate shared only by the matched test pair. It verifies every draft asset before making the stable tag visible to `/releases/latest`; it is not a substitute for the persistent project signing identity.

The v0.3.x exercise cannot silently cross into the v0.4.0 official channel because its ephemeral private key was intentionally destroyed. Users make that boundary transition by running the official v0.4.0 installer once. v0.4.0 establishes the persistent identity pinned by later automatic-update releases.

## GitHub signing secrets

The tag/manual Release workflow requires these repository secrets:

- `SIGNING_PFX_BASE64`: Base64 encoding of the PFX bytes
- `SIGNING_PFX_PASSWORD`: PFX password

The RFC 3161 timestamp URL is non-secret and fixed in the workflow to [DigiCert's documented `http://timestamp.digicert.com` endpoint](https://knowledge.digicert.com/general-information/rfc3161-compliant-time-stamp-authority-server). If either secret is absent, the official Release workflow fails before packaging. It temporarily trusts only the reviewed public certificate on the disposable runner, proves the PFX matches that certificate, validates all three Authenticode signatures, and removes the imported runner certificate and PFX afterward.

The current project certificate is self-signed, RSA-3072, code-signing-only, non-CA, valid through 13 August 2036, and has subject `CN=Achievement Relay Open Source`, exactly matching the permanent MSIX publisher. Keep a tested, encrypted offline backup of its PFX and password in separate protected locations: GitHub secrets cannot be read back. Never commit a PFX, password, Base64 private key, or replacement certificate pin before its controlled transition release.

## Checklist

1. Update application/file versions, `release/update-policy.json`, `CHANGELOG.md`, and the matching `docs/RELEASE-NOTES-X.Y.Z.md`.
2. Run the core contract checks and repository checks.
3. Build and install both target packages on representative Windows devices where available.
4. Verify installer connect-now/skip and Steam-only paths, encrypted handoff deletion, desktop-shortcut choice, first baselines, Discord test, and real Xbox account sync. Run two consecutive Xbox syncs and confirm that no pre-baseline or newly revealed historical achievement reaches Discord. Confirm historical Xbox titles consume no more than one background detail slot per 15 minutes, while a genuinely new modern unlock and one untimestamped Xbox 360 unlock each post exactly once across tray close/reopen. For Steam, baseline a history-heavy game, observe one real live unlock, verify a restart/offline unlock stays silent, simulate a failed webhook and verify its pending live transition retries once, and exercise the x64 helper/package check on every supported architecture available. Finish with startup, running-app update discovery/download, updater cancellation, `/UPDATE=1` state/shortcut preservation, required-policy monitoring suspension, and uninstall checks.
5. Confirm the package certificate subject, SHA-256 fingerprint, code-signing EKU, and validity match `release/publisher-certificate.json`.
6. Tag the verified commit with `v<major>.<minor>.<patch>` and push the tag, or run the **Release** workflow from GitHub Actions and enter that version. When `docs/RELEASE-NOTES-X.Y.Z.md` exists, the workflow uses it as the public release description; otherwise it generates notes. The manual workflow creates the tag at the selected commit when it publishes the release.
7. Confirm `AchievementRelay_Setup.exe`, `AchievementRelay_Update.json`, `AchievementRelay_Update.sig`, `AchievementRelay.Publisher.cer`, both MSIX packages, and the manual ZIP are attached to the release. Official releases must not contain a newly generated development `.cer`.
8. Download the published manifest and installer. Recompute the setup SHA-256/size, confirm the tag/product-version/package-version/support-floor contract, and verify signatures with `Get-AuthenticodeSignature` or `SignTool verify /pa`.
9. From the preceding installed release, launch the app and confirm it automatically discovers, downloads, verifies and opens the musical updater. Also test a required update discovered by a running app (monitoring pauses and Setup opens), an optional running-app update (it prepares without interrupting play and opens next launch), cancellation, and one injected failure (no automatic relaunch loop; explicit Retry still works). Verify the new app reports current and deletes its completed installer download.

Store onboarding may replace the local package identity and publisher. Treat those values as permanent after the first stable public distribution.
