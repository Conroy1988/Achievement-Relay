# Changelog

All notable changes to Achievement Relay are documented here. The project follows semantic versioning after the initial alpha series.

## [Unreleased]

### Added

- Added the original CRNY track **Relay Online** to Setup with local 10% playback, looping, persistent Play/Pause controls, clean shutdown, and a user-initiated SoundCloud link
- Added a **Get the relay** GitHub link to every Discord achievement and connection-test post
- Added local, keyless Steam achievement monitoring through an isolated Steamworks helper; the app automatically detects the active game and requires no Steam Web API key or Steam64 ID
- Added per-Steam-account/per-game complete snapshot baselines and monotonic unlocked-identity state, so an existing Steam library can never become a Discord history dump
- Added strict Steam live-transition proof from locked-to-unlocked observation or Steam's completed-achievement callback—never timestamps—plus durable pending-delivery recovery, shared cross-provider Discord deduplication/retry, local icon-to-PNG attachments, public cached global rarity, and platform/player metadata
- Added Steam-only installer and Guided setup paths: Discord is required, OpenXBL is optional, and Xbox plus Steam can run independently or together
- Added live Steam dashboard/settings/diagnostics controls, a helper protocol self-test, pinned dependency hash checks, both-architecture packaging, full research notes, privacy boundaries, failure policy, and a real-Windows release matrix

### Fixed

- Parsed Steam's global achievement percentages when the public endpoint represents `percent` as a JSON string as well as a number, restoring the percentage-based Rarity field in Discord embeds
- Encoded Steam artwork as Base64 on the helper JSON wire contract, fixing the first live-unlock snapshot being rejected when Steam supplied an icon; the bridge self-test now exercises this exact representation
- Restarted the isolated helper after unreadable protocol output, made optional Steam rarity failures non-blocking, repaired malformed provider Unicode before Discord serialization, and added privacy-safe processing-stage diagnostics while retaining durable pending-unlock recovery
- Explicitly requested the signed-in local Steam user's achievement stats after helper initialization and after any unload, fixing the observer's permanent pre-baseline wait when Facepunch's no-op `RequestCurrentStats()` produced no callback
- Refreshed the read-only local-user stats snapshot every ten seconds after baseline so achievements stored by the game or a separate Steamworks process become observable without relying on cross-process cache propagation
- Added bounded 20-second stats loading and 45-second complete-baseline watchdogs, structured recovery diagnostics, and truthful Connecting/Loading/Baselining/Monitoring phases so a detected game can never appear ready before its first complete observation
- Gave each pull-request installer an increasing MSIX revision so updated test builds install over earlier packages instead of failing with `0x80073CFB`
- Attempted direct shutdown of the running tray app and isolated Steam helper, then delegated any stubborn or elevated instance to Windows' package deployment broker instead of aborting the upgrade
- Switched account verification from OpenXBL's legacy host to the provider's current `api.xbl.io` service so a saved key can resolve the Xbox profile and complete setup
- Combined XUID and gamertag fields found across nested OpenXBL account envelopes instead of requiring both values in the same JSON object
- Switched title polling to OpenXBL's current-account `player/titleHistory` operation instead of the XUID-specific route that returns 404 for the connected key owner
- Added one-time, cached route negotiation across OpenXBL's current `/api/v2/` paths and its live `/v2/` compatibility paths, with a five-minute retry backoff if no title route is usable
- Accepted JSON collections wrapped as encoded strings inside provider response envelopes
- Added OpenXBL's dedicated Xbox 360 achievement route and accepted its `unlocked` response field
- Kept probing documented per-title detail routes when the first readable response contains fewer unlocked achievements than title history reports, then cached the complete route per title
- Replaced timestamp-only unlock detection with durable per-title achievement identity sets, so Xbox 360/backward-compatible unlocks with missing or `0001-01-01` times can post normally
- Added a fail-closed schema-v2/v3 to schema-v4 migration: count and Gamerscore changes can never infer an untimestamped post before a verified identity baseline
- Silently baselined titles first revealed after the original title-history page, preventing complete 2009-era and other historical game backlogs from reaching Discord
- Retained saved title snapshots when OpenXBL omits a title from a later page, preventing an old game from reappearing as a false new title
- Required exact title-index/detail agreement, followed documented continuation pages, and gradually hydrated every unverified count-only baseline without posting historical achievements
- Preserved durable counts and identities across regressive or representation-changing provider responses instead of risking a historical Discord flood
- Replaced the unbounded changed-title loop with a durable per-title queue that expands at most one title per sync, prioritises verified/recently played games, and gives historical hydration only one background slot every 15 minutes
- Added a 12-request ceiling to each title-detail operation, a conservative 120-request rolling-hour guard, provider `X-RateLimit-Remaining`/reset tracking, and protected reserves so history cannot consume the full OpenXBL allowance
- Honored the complete provider reset delay instead of retrying an hourly OpenXBL limit every 15 minutes
- Labelled Discord timestamps as detected/estimated when Xbox supplies no usable unlock time
- Requested Discord webhook responses with `wait=true`, honored both forms of `Retry-After`, canonicalized legacy webhook hosts, and disabled credential-bearing redirects
- Documented the researched OpenXBL response families, failure policy, security boundary, automated matrix, and real-Windows release gates

### Planned

- Stable code signing and update channel

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
