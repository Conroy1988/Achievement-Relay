# Achievement Relay v0.4.0

This is the first official Achievement Relay release: a focused Windows companion that relays newly proven Xbox and local Steam achievements to the Discord channel you choose.

## Download

For most users, download **`AchievementRelay_Setup.exe`** below. Setup automatically selects x64 or Arm64, preserves existing settings during an update, and includes the same guided experience on both architectures.

The versioned ZIP is a manual fallback for environments where the setup executable is restricted. Individual MSIX packages are also attached for managed or advanced installation.

## Highlights

- **A completely redesigned command-red interface.** The Home, Setup, Activity, Settings, and Help screens now use a crisp black, warm-white, and crimson visual system with clearer hierarchy and status language.
- **A new Achievement Relay identity.** The shield-and-trophy icon now appears consistently in the app, package, installer, tray, taskbar, and GitHub documentation.
- **Xbox and Steam in one relay.** Xbox monitoring uses your own OpenXBL key; local Steam monitoring is read-only and needs no API key, Steam64 ID, or account password.
- **Featured Community & Support.** Join the [TKB community Discord](https://discord.gg/3ZdXhYjgDm) directly from the sidebar or Help screen. Ko-fi support remains available and optional.
- **Verified self-updates.** Achievement Relay checks the official stable GitHub release at startup and about every six hours. It validates the signed manifest, asset identity, size, SHA-256, embedded versions, Windows Authenticode trust, and pinned publisher certificate before opening an updater.
- **Automatic required updates.** A reviewed signed release can raise the supported-version floor. Only an authenticated policy can pause monitoring and start the updater; offline or invalid checks fail safely.
- **Music in installs and updates.** CRNY's original **Relay Online** plays locally at 10% volume, with Play/Pause and a direct [SoundCloud link](https://soundcloud.com/daniel-conroy-224318319/crny-relay-online).
- **Anti-backlog protection.** Every Xbox title and Steam account/game pair receives a silent baseline before normal delivery. Historical or unprovable achievements are never turned into a Discord flood.

## Upgrading from a 0.3.x updater test

Install v0.4.0 manually once from this official release. The isolated 0.3.x updater test used an intentionally temporary signing identity, and its destroyed private key cannot authorize a move to the official persistent release identity. This is a signing-boundary transition, not a settings reset.

The installer preserves encrypted OpenXBL and Discord connections, Xbox and Steam baselines, the processed-event ledger, pending deliveries, startup preference, and desktop-shortcut choice. From v0.4.0 onward, verified automatic updates use the official persistent publisher identity.

## First-time setup

1. Run `AchievementRelay_Setup.exe`.
2. Choose Xbox, Steam, or both.
3. Add a Discord channel webhook. Add an OpenXBL key only if Xbox monitoring is selected.
4. Complete the four clear Setup steps, leave the app in the notification area, and play normally.

See [Getting Started](https://github.com/Conroy1988/Achievement-Relay/blob/main/GETTING_STARTED.md) for the complete walkthrough.

## Important boundaries

- Steam must be running before a Steam unlock. Offline Steam achievements join the next silent baseline by design.
- Steam games must expose achievements through Steamworks. Steam monitoring on Arm64 requires Windows 11 x64 emulation.
- Xbox delivery depends on Xbox sync and OpenXBL availability and allowance. OpenXBL is independent and unofficial.
- A new code-signing certificate may still trigger a Microsoft Defender SmartScreen reputation warning even when its signature is valid. Confirm the publisher and download only from this repository.
- Achievement Relay is not affiliated with Microsoft, Xbox, Valve, Steam, OpenXBL, or Discord.

## Release assets

- `AchievementRelay_Setup.exe` — recommended architecture-selecting installer and updater
- `AchievementRelay_Update.json` — signed-manifest payload with versions, asset identity, size, and SHA-256
- `AchievementRelay_Update.sig` — detached RSA signature envelope for the exact manifest bytes
- `AchievementRelay_0.4.0.0_x64.msix` — x64 Windows package
- `AchievementRelay_0.4.0.0_arm64.msix` — Arm64 Windows package
- `AchievementRelay_0.4.0.0_installer.zip` — manual installation fallback

Need help? Join [Community & Support on Discord](https://discord.gg/3ZdXhYjgDm). Never share an OpenXBL API key or Discord webhook URL.
