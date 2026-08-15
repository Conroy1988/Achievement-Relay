# Changelog

All notable changes to Achievement Relay are documented here. The project follows semantic versioning after the initial alpha series.

## [Unreleased]

### Planned

- Stable code signing and update channel
- Steam achievement provider research

## [0.2.1] - 2026-08-15

### Fixed

- Made installer-entered OpenXBL and Discord secrets survive first launch by storing the encrypted app settings before any network verification
- Moved the one-time encrypted handoff outside virtualized AppData while retaining the 0.2.0 path as a compatibility fallback
- Kept the encrypted handoff for a later retry if durable settings storage fails instead of consuming it prematurely
- Displayed stored OpenXBL and Discord values as masked fields with explicit Reveal/Hide controls in Guided setup and Settings
- Saved a manually entered OpenXBL key before network verification so a provider response failure does not make it appear lost
- Accepted OpenXBL account and title responses wrapped in current object, people, data, result, or response envelopes, including display-name fallbacks

### Changed

- Rebuilt the app dashboard as a cinematic relay command centre with original multi-genre achievement artwork
- Replaced the installer artwork with a premium trophy-and-game-world composition
- Added persistent secure-vault guidance so hidden saved values are never mistaken for missing values
- Added a compact Ko-fi action to the sidebar while keeping the full support card in About
- Added attributed CC BY 3.0 trophy and radar interface art, with a dedicated third-party notice
- Added explicit Windows 11 AppData virtualization exclusion alongside the existing full-trust package declaration

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

[Unreleased]: https://github.com/Conroy1988/Achievement-Relay/compare/v0.2.1...HEAD
[0.2.1]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.2.1
[0.2.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.2.0
[0.1.1]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.1.1
[0.1.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.1.0
