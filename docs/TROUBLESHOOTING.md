# Troubleshooting

Start with **Diagnostics → Sync Xbox now**. It tests the real OpenXBL → Achievement Relay → Discord path. **Send sample achievement** tests Discord only.

## OpenXBL rejects the API key

1. Open [OpenXBL Profile](https://xbl.io/profile).
2. Confirm the intended Xbox profile is connected.
3. Create/copy a current API key without leading or trailing spaces.
4. In Achievement Relay, open **Settings → Xbox account · OpenXBL**, paste the replacement, and choose **Retry / replace**.

Never paste the key into a GitHub issue. If it was exposed, revoke it before creating another.

If the key saves but setup says that no usable Xbox profile was returned, confirm the Xbox profile is connected on the OpenXBL profile page. Achievement Relay uses OpenXBL's current `api.xbl.io` service; the legacy `xbl.io` host is not used for account verification.

## OpenXBL is rate-limiting requests

Achievement Relay's default one-minute interval normally makes about 60 title-index checks per hour plus changed-title and occasional account/setup checks. If the same key is used by other software, or the account's OpenXBL allowance is reached, the provider can return HTTP 429. The app respects `Retry-After` when supplied and waits up to 15 minutes before retrying. OpenXBL's published OpenAPI file does not state a numeric allowance, so the app does not hard-code one.

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
2. Dashboard shows **Xbox Account: Connected**, **Discord: Connected**, and **Relay: Monitoring**;
3. **Diagnostics → Last successful sync** is recent;
4. **Sync Xbox now** reports success; and
5. **Activity** does not show provider, rate-limit, webhook, or rare-only messages.

No Windows Notification Center entry is required. Xbox can take time to add an offline or newly validated unlock to the account feed.

## Sync succeeds but says no new achievements

- Confirm OpenXBL is connected to the same Xbox profile used by the game.
- Check whether the achievement is visible in the Xbox mobile app/profile.
- Wait a few minutes and sync again; Xbox/OpenXBL can lag.
- Confirm **Only post rare achievements** is disabled unless intended.
- Remember that achievements older than the first connection baseline are intentionally not posted.

## Xbox 360 unlock has no timestamp

Current builds do not require an OpenXBL timestamp to detect an achievement. Xbox 360/backward-compatible responses can report an achieved identity with a missing or `0001-01-01` time. Achievement Relay compares stable achievement IDs, posts the new identity with the detection time, and labels that time as estimated in Discord.

When upgrading from a count-only test build, the first changed-title check also creates the identity baseline. If exactly one untimestamped identity explains the count increase, it is posted. If several historical untimestamped entries are indistinguishable, the app baselines them once to avoid an old-achievement flood; later unlocks for that title are exact and timestamp-independent. A repeating “no new timestamped achievement” warning indicates an obsolete build.

## Installer-provided credentials need attention

Setup only validates the local shape; the app stores both secrets before performing live checks after launch. If one fails, Guided setup opens with status details. Stored fields show masked saved values: use **Reveal Key** or **Reveal Webhook** to inspect one, choose **Save and connect** or **Save and test** to retry it, or type a replacement.

The one-time installer file contains DPAPI ciphertext, not plaintext. The app truncates and deletes it only after the normal encrypted settings file is safely written. If it remains after a crash, exit the app and remove `%USERPROFILE%\.achievement-relay\pending-installer-setup.json` (or the legacy `%LOCALAPPDATA%\AchievementRelay\pending-installer-setup.json`), then use Guided setup.

## Desktop shortcut was not created

Re-run `AchievementRelay_Setup.exe` and enable **Create a desktop shortcut**, or create a shortcut from the installed app's Start-menu entry. The manual `Install.ps1` supports `-CreateDesktopShortcut`.

## Windows says the package is already installed but the contents differ

Windows rejects a changed MSIX when it reuses an installed four-part package version. Current pull-request installers include an increasing test revision so each newer artifact performs an in-place upgrade. Download the newest artifact instead of retrying an older installer.

Setup first attempts to close any Achievement Relay instance running in the current Windows session, including the notification-area instance. If a packaged or elevated process remains, Setup no longer aborts or asks for manual closure: `Add-AppxPackage -ForceApplicationShutdown` delegates the final termination to Windows' package deployment broker. The upgrade retains encrypted per-user settings and relaunches the updated app afterward.

## Duplicate posts

Successful event IDs are retained locally for 90 days, capped at 1,000. Do not delete `processed-events.json` during ordinary troubleshooting. If duplicates persist, include approximate unlock/post times and the redacted support summary in an issue.

## Safe support report

Use **Diagnostics → Copy support summary**. It excludes the API key, webhook URL/token, XUID, and gamertag. Before posting, check it again for private data.

Never attach `settings.json`, `pending-installer-setup.json`, raw provider responses, or screenshots containing credentials. Revoke any secret that was exposed.
