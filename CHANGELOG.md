# Changelog

All notable changes to Achievement Relay are documented here. The project follows semantic versioning after the initial alpha series.

## [Unreleased]

No changes yet.

## [0.6.0] - 2026-08-30

### Added

- Added the compact top-centre **Signal Strip** overlay for newly proven Xbox and Steam achievements
- Added achievement artwork with the premium Relay fallback, the matching Bronze/Silver/Gold/Platinum/Unranked emblem, exact global unlock percentage, platform and Gamerscore to the local banner
- Added a default-on **Achievement overlay** preference that existing and new installations can disable without affecting Discord delivery

### Interaction and accessibility

- Made the overlay click-through, non-activating and silent so it cannot take mouse or keyboard control away from a game
- Added a five-second slide-in presentation and a bounded sequential queue so consecutive unlocks remain readable without banners stacking over gameplay
- Kept rarity meaning redundant through emblem shape, percentage and written tier treatment rather than relying on colour alone

### Reliability and privacy

- Reused the established live-unlock eligibility boundary: baselines, startup reconciliation, offline history and unproven provider changes cannot produce an overlay
- Kept all overlay composition local and added no account service, telemetry, screen capture or new network destination
- Preserved encrypted connections, provider baselines, pending Discord deliveries, deduplication, Collector Cards and the certificate-pinned automatic-update chain

## [0.5.0] - 2026-08-30

### Added

- Added full-width Discord Collector Cards with game/achievement artwork when available and a premium Achievement Relay fallback when it is not
- Added Bronze, Silver, Gold and Platinum Relay rarity emblems driven by validated global unlock percentages, plus a neutral Unranked treatment when a provider supplies no trustworthy percentage
- Added evidence-based Xbox platform labels for Xbox PC, Xbox Console and Xbox 360 while retaining the honest generic Xbox label for mixed or ambiguous titles
- Added the exact unlock percentage to the card and retained game, achievement, reward, player and platform information as accessible Discord embed text

### Changed

- Replaced the small achievement thumbnail presentation with one cohesive, locally rendered rarity card and tier-colored Discord treatment
- Routed sample-achievement delivery through the production card path so users can preview the real fallback design without earning another achievement
- Preserved Steam's local icon path and Xbox artwork enrichment while treating artwork as optional presentation rather than a delivery dependency

### Reliability, accessibility and security

- Bounded provider text, percentage values, image downloads, decoded dimensions and rendered output before building a Discord attachment
- Kept card meaning independent of color through distinct emblem silhouettes, internal marks, written tier names, attachment descriptions and ordinary text fields outside the image
- Fell back to a complete branded card when artwork is unavailable and to the accessible text embed if card rendering itself cannot complete
- Preserved Xbox/Steam baselines, live-delivery evidence, pending retry behavior, deduplication and the certificate-pinned automatic-update chain

## [0.4.3] - 2026-08-17

### Fixed

- Made Home the deterministic initial route in XAML, window construction and every normal visible launch, fixing the empty dark content surface that could appear after an updater relaunch
- Added a loaded-window recovery fallback when WPF reports no selected content tab, while preserving intentional Guided Setup and required-update navigation
- Added a repository regression guard that requires the complete startup-to-Home contract

## [0.4.2] - 2026-08-17

### Fixed

- Made verified updater launches silent by default while retaining opt-in **Play music**/**Pause music**, the fixed 10% volume limit, both playback backends, and the direct CRNY SoundCloud link
- Began a fresh Xbox live-delivery epoch on every app start and after long monitoring interruptions, silently reconciling achievements earned before that point so a PC started later cannot replay another device's posts
- Required direct same-session evidence before an untimestamped Xbox 360 identity can post, while continuing to accept usable timestamps strictly inside the current live session
- Persisted proven live-delivery evidence with queued Xbox work so provider or Discord failures remain safely retryable across an app or updater restart
- Failed closed when migrating older queued Xbox work and when the Windows clock moves backwards instead of inferring that historical progress is new

### Documentation

- Documented sequential device handoffs, the one-active-Xbox-relay recommendation, local-only deduplication boundaries, muted updater behavior, and the expanded Windows release test matrix

## [0.4.1] - 2026-08-16

### Fixed

- Removed native light `TabControl` chrome that could replace the intended dark content canvas with a white Windows-theme surface
- Fixed inherited system-black headings and activity text across every dark card, list, setup step, settings panel and support panel
- Separated accessible small red text from the darker brand/fill red and made hover, disabled, input-border, card-boundary and keyboard-focus states independently readable
- Raised every explicit interface label to at least 11 device-independent pixels and added UI Automation names/live status announcements to key controls
- Added repository-enforced WCAG contrast gates for primary, muted, semantic, button, hover, disabled and non-text boundary combinations

## [0.4.0] - 2026-08-16

### Release highlights

- Declared the first official Achievement Relay release and established its persistent, certificate-pinned update channel
- Rebuilt the full application around a crisp black, warm-white, and command-red theme with native dark window chrome and clearer readable status hierarchy
- Introduced the new shield-and-trophy Achievement Relay identity across the app, tray, package, installer, and GitHub artwork
- Added persistent **Community & Support** actions in the sidebar and Help screen using the TKB community Discord invite
- Added automatic verified update handling at launch and during runtime, including authenticated required-update enforcement, safe monitoring suspension, and update-loop protection
- Added complete official-release documentation, GitHub artwork, release notes, signing guidance, and the one-time pre-official v0.3.x transition path
- Established a persistent RSA-3072 project-owned signing identity, a reviewed public-certificate fingerprint, and the explicit one-time Windows Trusted People import used by every later automatic update

### Added

- Added the original CRNY track **Relay Online** to Setup with local 10% playback, looping, persistent Play/Pause controls, clean shutdown, and a user-initiated SoundCloud link
- Added a **Get the relay** GitHub link to every Discord achievement and connection-test post
- Added local, keyless Steam achievement monitoring through an isolated Steamworks helper; the app automatically detects the active game and requires no Steam Web API key or Steam64 ID
- Added per-Steam-account/per-game complete snapshot baselines and monotonic unlocked-identity state, so an existing Steam library can never become a Discord history dump
- Added strict Steam live-transition proof from locked-to-unlocked observation or Steam's completed-achievement callback—never timestamps—plus durable pending-delivery recovery, shared cross-provider Discord deduplication/retry, local icon-to-PNG attachments, public cached global rarity, and platform/player metadata
- Added Steam-only installer and Guided setup paths: Discord is required, OpenXBL is optional, and Xbox plus Steam can run independently or together
- Added live Steam dashboard/settings/diagnostics controls, a helper protocol self-test, pinned dependency hash checks, both-architecture packaging, full research notes, privacy boundaries, failure policy, and a real-Windows release matrix

### Fixed

- Added a Windows Media Player primary soundtrack backend with isolated 10% volume, real Pause/Play and looping, while retaining the independently volume-limited MCI fallback; this fixes **Music unavailable** on desktops where relying on the legacy MPEG device path alone failed
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

### Distribution

- Official packages now require the persistent project signing PFX, an exact match with the committed public certificate, and RFC 3161 timestamping; the release workflow fails closed instead of publishing a development-signed or identity-mismatched updater
- Pre-official v0.3.x updater-test builds require one manual v0.4.0 install because their deliberately ephemeral signing key was destroyed; encrypted settings and relay state are preserved

## [0.3.2] - 2026-08-16

### Fixed

- Published the corrected isolated automatic-update bridge after the v0.3.1 installer's padded Windows version resource exposed a strict comparison failure
- Kept the matched baseline and target on one temporary test identity so the complete GitHub latest-release discovery, manifest verification, required-update, download, and musical updater path could be exercised safely

## [0.3.1] - 2026-08-16

### Test release

- Introduced the isolated end-to-end automatic-update test baseline; superseded by v0.3.2 after the immutable installed updater revealed version-resource padding behavior

## [0.3.0] - 2026-08-16

### Test milestone

- Introduced local Steam achievement monitoring and the first certificate-pinned GitHub updater implementation before the official v0.4.0 channel was established

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

[Unreleased]: https://github.com/Conroy1988/Achievement-Relay/compare/v0.6.0...HEAD
[0.6.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.6.0
[0.5.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.5.0
[0.4.3]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.4.3
[0.4.2]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.4.2
[0.4.1]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.4.1
[0.4.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.4.0
[0.3.2]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.3.2
[0.3.1]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.3.1
[0.3.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.3.0
[0.2.1]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.2.1
[0.2.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.2.0
[0.1.1]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.1.1
[0.1.0]: https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.1.0
