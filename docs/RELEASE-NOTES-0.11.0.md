# Achievement Relay v0.11.0

## Optional Discord account and encrypted sync

Sign in from **Settings → Relay account → Continue with Discord** to sync connection settings, presentation and sound preferences, per-game preferences, pins and recent achievement history across your PCs.

On your first PC, create and copy a recovery key to a private password manager, then select **Sync now**. On another PC, sign in with the same Discord account, unlock with that key and sync. Automatic sync runs every five minutes while Relay is in the tray and the Companion window is closed.

Your profile, including the Discord webhook and OpenXBL key, is encrypted on your PC before upload. The hosted service uses Supabase Free. Windows startup, monitor/position settings, shared-folder paths, artwork and Steam installation/account remain device-specific.

## Delivery and history

Signed-in devices coordinate delivery claims to avoid duplicate Discord posts. Cloud outages make posts wait; uncertain sends are held for manual checking. Synced history never becomes a new post or automatic overlay. Cloud history is bounded to 300 journal entries and 100 games with up to 30 imported achievements per game, within a 1.5 MB encrypted profile.

The Signal Strip overlay, Collector Card showcase, Xbox PC Game Pass labels and v0.4.0 minimum-supported update baseline are preserved.

## Installation and beta status

Use **AchievementRelay_Setup.exe**. The release includes the signed update manifest, publisher certificate, AchievementRelay_0.11.0.0_x64.msix, the ARM64 package and a fallback installer ZIP.

Account sync is an optional beta included in this official release. Automated encryption, merge, local-store and hosted account-isolation/claim checks passed. Real Discord sign-in and two-device testing are pending and will be performed on this release. Save the recovery key: the service cannot recover it. Signing out keeps local data and returns that PC to independent posting.

See [the account-sync guide](https://github.com/Conroy1988/Achievement-Relay/blob/main/docs/ACCOUNT-SYNC.md) for conflict handling, Free-plan limits and recovery details.
