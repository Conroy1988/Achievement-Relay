# Troubleshooting

Start with **Help & support**. **Sync Xbox now** tests OpenXBL → Achievement Relay → Discord. **Refresh Steam** restarts local game detection/observation. **Send Discord test** tests Discord only. For direct help and community feedback, select **Join Discord** in the app or visit [Community & Support](https://discord.gg/3ZdXhYjgDm). Never share an OpenXBL key or Discord webhook URL.

## Steam says Ready instead of Monitoring

**Ready** means the local observer is healthy but no game is detected. Start the game through the signed-in Windows Steam client and wait a few seconds. If the game is running:

1. choose **Help & support → Refresh Steam**;
2. expand **Technical status** and confirm the Steam installation and client were found;
3. confirm the game has Steam achievements on its Steam store/community page; and
4. copy the redacted support summary if the active game is still not detected.

Achievement Relay checks Steam's active App ID first and then falls back to matching running executable paths against installed Steam manifests. Launchers and anti-cheat transitions get a ten-second grace period.

## Steam stays on Preparing

**Preparing** means the game was detected but no complete safe baseline exists yet. Achievement Relay first connects its isolated observer, explicitly requests the signed-in local player's current stats, and verifies three stable achievement-schema reads. Do not test an unlock until Activity says **Steam baseline established** and Home changes to **Monitoring**.

The stats request has a 20-second deadline and the complete-baseline watchdog has a 45-second deadline. A stalled helper now reports an error and restarts instead of displaying a false Monitoring state indefinitely. If it keeps retrying, leave the game open, copy the redacted support summary, and attach `achievement-relay.log` without attaching settings or sync-state files.

## Steam baseline appeared but Discord stayed silent

That is expected. The first complete snapshot for each Steam account and game is a history baseline. Achievement Relay stores every already-unlocked API name without posting it. Unlock a different achievement after Activity reports **Steam baseline established**.

If Home changes to **Retrying** immediately after a real live unlock, open **Help & support**, copy the support summary, and attach `achievement-relay.log`. Current builds restart malformed helper output, identify the safe processing stage without exposing account or webhook data, and keep an accepted live transition in `steam-sync-state.json` before rarity or Discord work. Installing a corrected build and relaunching the same game retries that pending achievement; do not delete the sync-state or processed-event files and do not earn another achievement merely to trigger recovery.

The first complete snapshot after an app/helper restart is also treated as history. A pre-baseline unlock qualifies only when Steam emits its direct completed-achievement callback during that helper session; a recent timestamp alone never qualifies. Steam achievements earned while Achievement Relay was closed are therefore silent by design, preventing an offline backlog from being mistaken for live unlocks.

Never delete `steam-sync-state.json` to force a test: doing so deliberately creates another silent baseline. Use **Send sample achievement** to test Discord.

## A new Steam achievement did not post

1. Confirm Home showed **Steam: Monitoring** and Activity recorded **Steam baseline established** before the unlock.
2. Check Activity for a complete baseline, Steamworks retry, Discord failure, or rare-only message.
3. Leave the game open and select **Refresh Steam**.
4. If Activity already recorded the live transition before Discord failed, the durable pending delivery will retry; an unlock earned while Achievement Relay was closed is intentionally silent.
5. If this was the first-ever monitored session and the unlock happened before the complete baseline, it remains silent by design because the app cannot safely distinguish it from old history.

Some games do not publish achievements through Steamworks or have broken/delayed offline stats. Achievement Relay never guesses that an unobservable or pre-baseline history item is new.

## Steamworks keeps retrying

Keep the normal Steam desktop client signed in and launch the game through Steam. The helper initializes only for the detected active App ID, then explicitly requests the signed-in local player's stats because it starts after the game process. Steam family/account switching, Steam offline mode, a game update, anti-cheat, or a launcher can delay that response. The helper retries without advancing state or posting history.

If **Help & support → Technical status** says the Steam monitoring component is missing, reinstall the same or newer complete `AchievementRelay_Setup.exe`; do not copy only the main executable. Every package must contain `SteamBridge\AchievementRelay.SteamBridge.exe`, `Facepunch.Steamworks.Win64.dll`, and `steam_api64.dll`.

## OpenXBL rejects the API key

1. Open [OpenXBL Profile](https://xbl.io/profile).
2. Confirm the intended Xbox profile is connected.
3. Create/copy a current API key without leading or trailing spaces.
4. In Achievement Relay, open **Settings → Xbox account · OpenXBL**, paste the replacement, and choose **Retry / replace**.

Never paste the key into a GitHub issue. If it was exposed, revoke it before creating another.

If the key saves but setup says that no usable Xbox profile was returned, confirm the Xbox profile is connected on the OpenXBL profile page. Achievement Relay uses OpenXBL's current `api.xbl.io` service; the legacy `xbl.io` host is not used for account verification.

## OpenXBL is rate-limiting requests

OpenXBL currently publishes a free-plan limit of 150 requests/hour, and every HTTP request—including an error or cached response—counts. Achievement Relay's default one-minute interval normally makes about 60 lightweight title-index checks per hour. It processes at most one detailed title per sync, limits each multi-route/paged detail operation to 12 requests, and gives old-history hydration only one background slot every 15 minutes.

The app reads `X-RateLimit-Remaining` and reset headers when OpenXBL supplies them, keeps capacity reserved for live monitoring, and also enforces a conservative 120-request rolling-hour ceiling. On HTTP 429 it honors the full `Retry-After`/reset window rather than repeatedly probing. **Sync now** never bypasses these protections.

Check [OpenXBL pricing](https://xbl.io/pricing) and provider status. Do not launch multiple copies or reuse one key across many polling tools.

## Xbox account connects but the achievement feed fails

OpenXBL profile lookup, title-history lookup, and per-title achievement lookup are separate checks. Current builds ask for the API key owner's title history first instead of appending the XUID to that operation. They try OpenXBL's documented `/api/v2/` routes, including the canonical player/title and dedicated Xbox 360 detail operations, then compatible live routes. A readable detail response is accepted only when its unlocked count catches up with title history; the complete route is cached for that individual title. If every compatible title route fails, background checks wait five minutes before probing again. Install the newest build, confirm internet access to `api.xbl.io`, and retry **Sync Xbox now**. If it still fails, copy the redacted support summary and open an issue without attaching raw account JSON.

## Discord sample/test fails

1. Copy a fresh webhook from **Discord Server Settings → Integrations → Webhooks**.
2. In **Settings**, paste it and select **Test Discord**.
3. Confirm the webhook still exists and targets the intended channel.
4. Confirm the firewall, DNS filter, VPN, or proxy allows HTTPS to Discord.

HTTP 401 or 404 normally means the webhook was deleted, rotated, or copied incorrectly. Create a replacement. HTTP 429 is retried according to Discord's response.

## Game Bar showed the unlock but Discord received nothing

A Game Bar overlay is no longer the capture source. Check:

1. the tray icon is present;
2. Home shows **Xbox: Connected**, **Discord: Connected**, and **Relay: Ready**;
3. **Help & support → Technical status → Last Xbox sync** is recent;
4. **Sync Xbox now** reports success; and
5. **Activity** does not show provider, rate-limit, webhook, or rare-only messages.

No Windows Notification Center entry is required. Xbox can take time to add an offline or newly validated unlock to the account feed.

## Sync succeeds but says no new achievements

- Confirm OpenXBL is connected to the same Xbox profile used by the game.
- Check whether the achievement is visible in the Xbox mobile app/profile.
- Wait a few minutes and sync again; Xbox/OpenXBL can lag.
- Confirm **Only post rare achievements** is disabled unless intended.
- Remember that achievements older than the first connection baseline are intentionally not posted.
- If the app had just started or resumed after a long interruption, progress from before that new live-delivery epoch was intentionally baselined without posting. This prevents another PC from replaying achievements already relayed elsewhere.

## Xbox 360 unlock has no timestamp

Xbox 360/backward-compatible responses can report an achieved identity with a missing or `0001-01-01` time. Achievement Relay compares stable achievement IDs and can post a missing-time identity only when its count change was directly observed after a successful poll in the same uninterrupted live session. Discord then labels the detection time as estimated. A missing-time identity first found at app startup or after a long gap is baselined silently because it cannot be distinguished safely from another device's earlier unlock.

When upgrading from a count-only build, or when OpenXBL reveals an old title that was absent from the original baseline page, the first complete detail response creates the identity baseline. Counts and Gamerscore never authorize a post. A usable timestamp strictly after the current live-delivery epoch may prove a genuinely new event; old, missing-time, sentinel-time, and otherwise unproven entries are stored silently. A repeating backlog or “no new timestamped achievement” warning indicates an obsolete build; exit it from the notification area and install the latest official release.

## Installer-provided credentials need attention

Setup only validates local shape; the app stores the Discord webhook and any optional Xbox key before performing live checks after launch. Steam-only setup leaves the OpenXBL field blank. If optional Xbox fails, Steam and Discord can still start while the in-app Setup flow retains the masked key for retry. Use **Reveal Key** or **Reveal Webhook** to inspect a stored value, choose the relevant retry action, or type a replacement.

The one-time installer file contains DPAPI ciphertext, not plaintext. The app truncates and deletes it only after the normal encrypted settings file is safely written. If it remains after a crash, exit the app and remove `%USERPROFILE%\.achievement-relay\pending-installer-setup.json` (or the legacy `%LOCALAPPDATA%\AchievementRelay\pending-installer-setup.json`), then use Setup.

## Desktop shortcut was not created

Re-run `AchievementRelay_Setup.exe` and enable **Create a desktop shortcut**, or create a shortcut from the installed app's Start-menu entry. The manual `Install.ps1` supports `-CreateDesktopShortcut`.

## Windows says the package is already installed but the contents differ

Windows rejects a changed MSIX when it reuses an installed four-part package version. Current pull-request installers include an increasing test revision so each newer artifact performs an in-place upgrade. Download the newest artifact instead of retrying an older installer.

Setup first attempts to close any Achievement Relay instance running in the current Windows session, including the notification-area instance. If a packaged or elevated process remains, Setup no longer aborts or asks for manual closure: `Add-AppxPackage -ForceApplicationShutdown` delegates the final termination to Windows' package deployment broker. The upgrade retains encrypted per-user settings and relaunches the updated app afterward.

## Duplicate posts

Successful event IDs are retained locally for 90 days, capped at 1,000. Do not delete `processed-events.json` during ordinary troubleshooting.

From v0.4.2, every Xbox app start and long monitoring gap begins a fresh delivery epoch. Achievements unlocked before that point are silently reconciled, so starting a second PC later does not relay the first PC's achievements again. The ledger is not cloud-synced and Achievement Relay runs no hosted account service; keep Xbox monitoring active on only one PC at a time because two simultaneously active PCs can still race on the same live account change. If duplicates persist outside that simultaneous case, include approximate unlock/post times, which PCs were active, and the redacted support summary in an issue.

## Safe support report

Use **Help & support → Copy support summary**. It excludes the API key, webhook URL/token, XUID, gamertag, Steam account ID, and Steam player name. Before posting, check it again for private data.

Never attach `settings.json`, `pending-installer-setup.json`, `steam-sync-state.json`, raw provider responses, or screenshots containing credentials/account identifiers. Revoke any secret that was exposed.
