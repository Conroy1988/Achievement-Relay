# Steam integration research and reliability contract

Research frozen: 2026-08-16. This document records the evidence, design decisions, safety invariants, failure policy, and Windows release gates for Achievement Relay 0.3. It is the implementation contract, not a claim that Valve or every game guarantees identical behavior forever.

## Decision

Achievement Relay monitors Steam locally. It does not ask for a Steam Web API key, Steam64 ID, password, browser cookie, or OAuth token.

When a Steam game starts, the main app resolves its App ID from the signed-in Windows user's Steam installation and launches an isolated x64 helper for that App ID. The helper initializes the local Steamworks client, reads `ISteamUserStats` achievement identifiers and state, and emits complete snapshots over redirected standard output. The main app—not the helper—owns baseline state, deduplication, filtering, Discord delivery, retry, and logging.

The helper is x64 in both the x64 and Arm64 packages. [Microsoft's Windows on Arm documentation](https://learn.microsoft.com/windows/arm/apps-on-arm-x86-emulation) confirms that Windows 11 emulates x86 and x64 apps while Windows 10 on Arm emulates x86 only. The Windows 11 package therefore runs that small helper through x64 emulation while the main WPF app remains native Arm64; the UI explicitly marks Steam unavailable on Windows 10 Arm64 while Xbox remains usable. This avoids shipping a second self-contained .NET desktop runtime. The helper targets .NET Framework 4.8, which is part of the supported Windows 10 version 2004+ x64 platform.

## Evidence reviewed

### Proven reference application

[Steam Achievement Notifier](https://github.com/SteamAchievementNotifier/SteamAchievementNotifier) demonstrates the useful product shape: local Steam achievement observation, initial cache/baseline behavior, rich notifications, and Discord webhook delivery. Its most important lesson is that personal Steam history does not require the Web API when the signed-in local Steamworks context is available.

Achievement Relay does not copy SAN code or its Electron architecture. It implements a narrow, independently designed observer and keeps the existing .NET delivery and durable-state pipeline.

### Valve contracts

- [SteamAPI initialization](https://partner.steamgames.com/doc/api/steam_api) defines the local client initialization and callback lifecycle.
- [ISteamUserStats](https://partner.steamgames.com/doc/api/ISteamUserStats) exposes current-user achievement state, display attributes, unlock state/time, icons, and global-percentage operations for the active App ID.
- [Steam achievements](https://partner.steamgames.com/doc/features/achievements) explains that games define achievements by stable API names and store player state through Steam user stats.
- [Steam Web API overview](https://partner.steamgames.com/doc/webapi_overview) distinguishes publisher/user-authenticated web operations from public interfaces. Achievement Relay does not use a personal key.
- Steam's public `ISteamUserStats/GetGlobalAchievementPercentagesForApp` endpoint is used only to enrich a newly detected unlock with rarity. The response is cached once per App ID for the process lifetime. Failure never blocks the unlock.

### Discord contract

[Discord webhook execution](https://docs.discord.com/developers/resources/webhook#execute-webhook) supports JSON embeds and multipart file uploads. Achievement Relay sends `payload_json` plus `files[0]`, then references local Steam artwork as `attachment://steam-achievement.png`. `allowed_mentions.parse` is always empty.

### Managed wrapper and supply chain

The helper uses [Facepunch.Steamworks](https://github.com/Facepunch/Facepunch.Steamworks), release 2.5.2, whose package identifies upstream commit `5a22fa22dd8e337e9fa55ce0d18c07c022262063`. It is MIT licensed.

The reviewed wrapper source sets `SteamAppId`/`SteamGameId` for the requested App ID during `SteamClient.Init`, initializes the client interfaces, and pumps callbacks asynchronously by default. Facepunch 2.5.2 intentionally implements `RequestCurrentStats()` as a no-op because the normal game process is hydrated by Steam before launch. Achievement Relay's observer is different: it is a separate process launched after the game. It therefore calls the wrapper's public `Friend.RequestUserStatsAsync()` for the signed-in local Steam ID and requires that explicit request to complete before reading even one achievement state. The request has a 20-second deadline and is repeated after `UserStatsUnloaded_t`; a stalled request exits the helper for a bounded restart instead of waiting forever. Valve documents both that `Result.Fail` can mean a user simply has no saved stats and that `RequestUserStats` snapshots are not updated automatically. Completed response/callback arrival—not an OK-only check—is therefore the safe readiness boundary for a brand-new player, and the helper refreshes the read-only local-user snapshot every ten seconds after its baseline so changes stored by the game or another local Steamworks process become visible. Each observation uses the union of the current-user and explicitly refreshed local-user read surfaces. Display text comes from read-only attributes, unlock times come from `GetAchievementAndUnlockTime`/`GetUserAchievementAndUnlockTime`, and icons come from the Steam image API. Although the wrapper also exposes mutation methods, the isolated helper does not call or reference them; repository checks reject `Trigger`, `Clear`, and `StoreStats` usage.

The reviewed package is committed under `third_party/packages` so a future registry mutation or outage cannot silently change release input:

- reviewed upstream release archive SHA-256: `83ef0b8b07bd5545c3732c65011f0baa9bf003cb53c2279c56397270368bca22`;
- NuGet package extracted from that reviewed release and committed here: `11e12d1b34d22a6c7ed6b5f70fd145f4794fc9b4c5fc9c5b380eb73b02b7571e`;
- exact MIT text: `third_party/Facepunch.Steamworks.LICENSE.txt`.

The repository check enforces the committed package hash. `NuGet.config` maps this dependency to the local source. Release packaging fails if the helper executable, managed wrapper, or Valve `steam_api64.dll` is absent.

## Detection path

1. Read `HKCU\Software\Valve\Steam\ActiveProcess\RunningAppID`.
2. Parse all current `steamapps/libraryfolders.vdf` paths, including legacy and current layouts.
3. Resolve `appmanifest_<AppID>.acf` for the game name and install directory.
4. If the active registry value is late or absent, compare running executable paths with a one-minute cached manifest catalog.
5. Keep the current game through a ten-second process/detection grace period so a launcher transition does not tear down monitoring.
6. Start one helper for that App ID; restart it after failure with a bounded delay; close it when the game exits or the app shuts down.

The helper never invokes achievement mutation methods. Its source uses only identity/name/description/state/unlock-time/icon reads.

## Anti-backlog invariants

These rules are non-negotiable:

1. A snapshot is accepted only when the protocol version matches, Steam ID is valid, App ID matches the detected game, identities are unique, and the declared count equals the complete collection.
2. The helper explicitly requests the local user's stats, never reads or emits achievement state while that request is loading/unloaded, and then waits for three stable nonempty achievement-schema reads before the initial snapshot. A completed empty-player response remains a valid readiness boundary. An unload-generation marker is checked on every polling tick and triggers a fresh bounded request; a changed Steam account ID is a second reset boundary, so even an unload/reload completed between ticks cannot compare two account sessions. This prevents an early all-locked cache or account switch from turning into a false backlog while still supporting a brand-new user with no saved stats.
3. With no saved state for the Steam account/App ID, the first complete snapshot is a baseline. Historical unlocked IDs are stored but never sent.
4. The helper subscribes to Steam's completed-achievement callback before initialization. A `0/0` completion received during that helper lifetime is direct live proof and can safely close the launch-to-baseline race.
5. Every helper process labels its first complete snapshot. Merely appearing unlocked on that or any later snapshot is history, regardless of how recent its timestamp looks.
6. Eligibility requires an API name absent from the durable set plus direct proof from either Steam's current-session completed-achievement callback or the helper's in-memory locked-to-unlocked observation. Appearance and timestamps alone are never proof.
7. Eligible identities enter a durable pending-delivery set before any Discord call. Accepted, filtered, or already-ledgered identities are then removed; provider/network failures retain them across helper and app restarts.
8. Saved unlocked IDs are monotonic: a provider reset, relock, partial response, or local achievement clear cannot remove an old ID and make it post again later.
9. A deterministic event ID is SHA-256 over provider marker, Steam account ID, App ID, and achievement API name. The shared processed-event ledger is a second duplicate barrier.
10. Switching Steam accounts starts a separate state namespace. The new account's first complete snapshots are baselines.

The helper repeats directly proven transitions on complete heartbeats for its lifetime. The main app keeps draining those heartbeats after a transient local-state or delivery exception, so proof is retried until it becomes durable instead of being discarded with a helper restart.

This policy deliberately prefers a missed unprovable or offline event over a historical flood. Unlocks earned while Achievement Relay is closed are folded silently into the next initial snapshot; only pending webhook delivery of an already observed live transition is recovered across restarts.

## Rarity, artwork, and timestamps

- A global percentage at or below 10% is labeled rare.
- If the public rarity request fails or omits the achievement, rarity is unknown. **Rare only** never discards an unknown-rarity unlock.
- Artwork is read locally only for a newly observed helper transition, limited to 512×512 RGBA, converted to PNG by the platform-neutral core, and uploaded directly to Discord. Artwork failure cannot lose the event.
- Helper artwork bytes cross the JSON protocol as an explicitly tested Base64 string, matching the main app's `System.Text.Json` byte-array contract; the raw decoded byte count remains the snapshot budget.
- Steam's unlock time is display metadata only and is used when valid. It never authorizes delivery. If absent or unusable, the local observation time is shown and the Discord footer labels it as detected/estimated.
- Steam has no Xbox-style Gamerscore, so the field is omitted.

## Privacy and network boundary

Local reads:

- Steam install registry values;
- `libraryfolders.vdf` and `appmanifest_*.acf`;
- running process executable paths for fallback game detection;
- current-user achievement state exposed by the local Steamworks client.

Durable local state:

- Steam account ID;
- per-App-ID game name, monitoring start/last-observed times, unlocked API-name set, and pending live-transition identities;
- processed deterministic event IDs and normal bounded activity logs.

The Steam account ID and local player name are never included in the copied support summary. No Steam credentials are collected. Outbound Steam traffic is limited to the optional public global-rarity request after a new unlock. Discord receives the achievement fields and optional icon selected by the user through their webhook.

## Failure policy

- Steam not installed or not running: show **Ready/Waiting**; retry detection without pop-up spam.
- No active game: remain ready and make no Steam network request.
- Steamworks initialization failure: show one deduplicated warning, let the helper exit, and restart after five seconds while the game remains active.
- Stats not ready: explicitly request the local account record; after 20 seconds emit a structured error and restart without publishing an empty baseline.
- No complete initial snapshot: the main app keeps the status at **Preparing**, reports a 45-second watchdog error, and restarts the isolated helper.
- Incomplete/duplicate snapshot: post nothing, save nothing, and retry.
- Unreadable or oversized helper output: terminate and restart only the isolated helper; a directly proven transition already persisted by the app remains pending.
- Helper protocol mismatch or missing packaged files: show a reinstall-required diagnostic.
- Discord/network failure: retain the durably pending live identity and retry it without reclassifying history, using bounded 1/2/5/15/30-minute backoff after the normal short transport retries.
- Rarity, icon, or malformed provider-text failure: discard or repair only that enrichment and continue with the proven achievement.
- App/game exit: attempt graceful helper shutdown, then terminate its isolated process if required.

## Automated contract matrix

Core checks prove:

- first snapshots baseline old unlocks silently;
- a callback-proven launch-race unlock is isolated from history;
- a directly observed locked-to-unlocked API name posts once;
- restart silently baselines an offline unlock and does not repost a known identity;
- a pending live transition is durable before Discord delivery;
- recent or future-skewed timestamps cannot escape the baseline;
- event IDs are deterministic and account/App-ID scoped;
- RGBA artwork produces a valid PNG container;
- Discord includes Steam platform/player/rarity/attachment metadata and suppresses mentions.

Repository checks additionally enforce the dependency hash, explicit local-user stats bootstrap, bounded startup watchdog, helper files, package wiring, Steam-only installer path, truthful UI phases, and anti-backlog source invariants. CI builds the .NET Framework helper, executes a JSON protocol self-test that proves successful, empty-player, and timed-out stats-request gates, builds both MSIX architectures, and retains the signed test installer.

## Real-Windows release gates

Before a production release, test on x64 and Windows on Arm where hardware is available:

1. Fresh Steam-only install with Discord configured and OpenXBL blank.
2. Upgrade from 0.2 with the tray app already running.
3. Existing game with many historical achievements: Dashboard remains **Preparing** until baseline activity appears, then changes to **Monitoring** while Discord stays silent.
4. New achievement after baseline: exactly one Discord post with correct game, player, time, rarity when available, and icon when available.
5. Restart app/game: no duplicate.
6. Unlock while Discord is unreachable, restore network, and verify one retry post.
7. Close Achievement Relay, earn an unlock in a previously baselined game, reopen/launch the game, and verify it is silently baselined rather than posted as a backlog item.
8. Switch Steam accounts: first snapshots for the second account remain silent.
9. Game with launcher/anti-cheat transition: monitor survives the grace period.
10. Steam offline mode, game with no achievements, hidden achievement, absent rarity, and absent icon: no crash or false post.
11. Steam and Xbox enabled together: serialized Discord delivery and independent provider status.
12. Remove the webhook: both providers stop; restoring it resumes without a history flood.

## Known external constraints

No client can guarantee observations that Steam itself does not expose. A game must publish achievements through Steamworks, the signed-in client must make current-user stats available, and Windows must permit the helper process. Game-specific anti-cheat, launcher, offline-sync, or broken achievement implementations can delay or prevent visibility. The app therefore fails closed—never guessing that history is new—and exposes diagnostics instead of advancing unsafe state.
