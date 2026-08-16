# Controlled live-update test

The `Controlled live updater test` workflow proves the complete production discovery and automatic updater-launch route without reusing or publishing a private production signing key. It builds two installers from the reviewed triggering commit on one disposable Windows runner:

- corrected bridge baseline product `0.3.1`, package `0.3.1.1`, containing the version-resource normalization and automatic updater and compiled to preserve the existing installation without reopening onboarding;
- required target product/package `0.3.2` / `0.3.2.0`.

Both installers and the signed update manifest use one ephemeral RSA code-signing certificate. The certificate is embedded in both test installers, so a clean Windows PC receives the normal one-time administrator trust prompt. The private key never leaves the runner.

For this controlled test, the reviewed `release/live-update-test-policy.json` sets `minimumSupportedVersion` to `0.3.2`. The bridge baseline must therefore pause Xbox and Steam monitoring, automatically download and verify the target, and open the updater without an **Update now** click. The workflow uploads and checks every asset while the release is still a draft, then publishes `v0.3.2` as a non-draft, non-prerelease release because GitHub's `/releases/latest` endpoint intentionally excludes drafts and prereleases.

The previously published `v0.3.1` test is immutable. Its installed `0.3.0` updater rejects legitimate trailing padding returned by Windows for Inno Setup's textual version resources. A user who installed that baseline must manually install the corrected bridge once; every subsequent verified update follows the automatic path.

## Test steps

1. Open the `v0.3.2` release and download `AchievementRelay_Baseline_Setup.exe`.
2. Close the older app, run the bridge installer, and approve the one-time test-certificate trust prompt if Windows displays it.
3. Let the bridge app open and do not press **Update now**. Confirm that it identifies `v0.3.2` as required, pauses monitoring, downloads and verifies it, then opens Setup automatically.
4. Confirm the updater starts the CRNY soundtrack at 10% volume and provides Pause/Play plus the direct SoundCloud link.
5. Cancel Setup once. Confirm the bridge remains open and paused, with no automatic relaunch loop.
6. Select **Install update**, complete the updater, and confirm Achievement Relay reports v0.3.2.
7. Confirm encrypted connections, preferences, activity state, startup behavior, and the desktop shortcut survive the update.
8. Confirm **Check now** reports the installed app as current and the completed installer download has been removed.

This development-certificate release is for the controlled updater test only. Do not redistribute it as a production build. A production release must use the persistent, trusted signing identity described in `RELEASING.md`.
