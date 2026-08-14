# Changelog

All notable changes to Achievement Relay are documented here. The project follows semantic versioning after the initial alpha series.

## [Unreleased]

### Planned

- Real-world redacted parser fixtures and additional Windows languages
- Stable code signing and update channel
- Steam achievement provider research

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

[Unreleased]: https://github.com/Conroy1988/Achievement-Relay/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.1.0
