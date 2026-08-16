# Controlled live-update test

The `Controlled live updater test` workflow proves the complete production discovery and install route without reusing or publishing a private production signing key. It builds two installers on one disposable Windows runner:

- baseline `0.3.0.0` from commit `ed80821ed8ec351fb5a010c7324eaa1a31cd2f5d`, which contains the pre-redesign UI and the secure updater;
- target `0.3.1.0` from the triggering commit, which contains the redesigned UI.

Both installers and the signed update manifest use one ephemeral RSA code-signing certificate. The certificate is embedded in both test installers, so a clean Windows PC receives the normal one-time administrator trust prompt. The private key never leaves the runner.

For this controlled test, the reviewed `release/live-update-test-policy.json` sets `minimumSupportedVersion` to `0.3.1`. A baseline `0.3.0` app must therefore show a required-update state and pause Xbox and Steam monitoring until the update completes. The workflow uploads and checks every asset while the release is still a draft, then publishes `v0.3.1` as a non-draft, non-prerelease release because GitHub's `/releases/latest` endpoint intentionally excludes drafts and prereleases.

## Test steps

1. Open the `v0.3.1` release and download `AchievementRelay_Baseline_Setup.exe`.
2. Run it and approve the one-time test-certificate trust prompt if Windows displays it.
3. Let the pre-redesign app open. Confirm that it identifies `v0.3.1` as required and says monitoring is paused.
4. Select **Update now**.
5. Confirm the updater starts the CRNY soundtrack at 10% volume and provides Pause/Play plus the direct SoundCloud link.
6. Complete the update and confirm the redesigned Home screen and four-step Setup experience open.
7. Confirm encrypted connections, preferences, activity state, startup behavior, and the desktop shortcut survive the update.
8. Confirm **Check now** reports the installed app as current and the completed installer download has been removed.

This development-certificate release is for the controlled updater test only. Do not redistribute it as a production build. A production release must use the persistent, trusted signing identity described in `RELEASING.md`.
