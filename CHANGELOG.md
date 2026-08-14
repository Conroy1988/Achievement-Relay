# Changelog

All notable changes to Achievement Relay are documented here. The project follows semantic versioning after the initial alpha series.

## [Unreleased]

### Planned

- Stable code signing and update channel
- Steam achievement provider research

## [0.2.0] - 2026-08-15

### Changed

- Replaced Windows Notification Center capture with one-minute Xbox account polling through a user-supplied OpenXBL API key
- Removed the obsolete `userNotificationListener` capability, Game Bar text parser, and notification permission screens
- Migrated 0.1.x settings while preserving the encrypted Discord webhook and reopening Guided setup for the new Xbox connection
- Reworked the WPF interface and installer around a shared dark, neon gaming theme

### Added

- OpenXBL account/profile verification, per-title progress change detection, Xbox Achievement v2 detail parsing, and safe provider error handling
- First-run baseline protection, offline unlock recovery, deterministic deduplication, manual sync, and Discord delivery retry
- Current-user DPAPI protection for the OpenXBL key with separate entropy from the webhook
- Optional installer fields for the OpenXBL key and Discord webhook, plus a clear configure-later path
- One-time DPAPI-encrypted installer handoff that avoids command-line secrets and is deleted by the app on first launch
- Installer desktop-shortcut toggle and matching uninstall cleanup
- Updated privacy, security, troubleshooting, architecture, onboarding, and search-focused project documentation

### Fixed

- Achievements that appear only in the Xbox Game Bar overlay can now be found through the account feed; version 0.1.x could not observe those overlays
- Failed Discord deliveries no longer advance the account cursor and are retried without duplicate successful posts

## [0.1.1] - 2026-08-14

> Legacy: this release cannot detect achievements that appear only in the Xbox Game Bar overlay. Upgrade to 0.2.0.

### Added

- Single-file `AchievementRelay_Setup.exe` with bundled x64 and Arm64 packages
- Clear installer errors and an automatic launch into Guided setup

### Fixed

- Native Arm64 detection when setup runs through an emulated process
- Installation guidance that previously required extracting a ZIP and manually running PowerShell

## [0.1.0] - 2026-08-14

### Added

- Windows Xbox/Game Bar notification monitoring through `UserNotificationListener`
- Exact Microsoft Xbox package-family source allowlist before notification text access
- Localized unlock phrase and Gamerscore parsing
- Discord webhook validation, rich embeds, mention suppression, rate-limit handling, and bounded retry
- Current-user DPAPI protection for webhook storage
- Ninety-day/1,000-entry achievement deduplication ledger
- Four-step first-run setup, dashboard, settings, diagnostics, activity view, and system tray
- Packaged and unpackaged Windows startup support
- x64/Arm64 MSIX packaging, installer scripts, development signing, CI, and tagged release workflow
- User, privacy, security, troubleshooting, architecture, contributor, and release documentation

[Unreleased]: https://github.com/Conroy1988/Achievement-Relay/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.2.0
[0.1.1]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.1.1
[0.1.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.1.0
