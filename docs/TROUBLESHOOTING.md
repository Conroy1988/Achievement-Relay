# Troubleshooting

Start with **Diagnostics → Sync Xbox now**. It tests the real OpenXBL → Achievement Relay → Discord path. **Send sample achievement** tests Discord only.

## OpenXBL rejects the API key

1. Open [OpenXBL Profile](https://xbl.io/profile).
2. Confirm the intended Xbox profile is connected.
3. Create/copy a current API key without leading or trailing spaces.
4. In Achievement Relay, open **Settings → Xbox account · OpenXBL**, paste the replacement, and choose **Retry / replace**.

Never paste the key into a GitHub issue. If it was exposed, revoke it before creating another.

## OpenXBL is rate-limiting requests

Achievement Relay's default one-minute interval normally uses about 60 achievement requests per hour plus occasional account/setup checks. If the same key is used by other software, or OpenXBL changes the plan limit, the provider can return HTTP 429. The app respects `Retry-After` when supplied and waits up to 15 minutes before retrying.

Check [OpenXBL pricing](https://xbl.io/pricing) and provider status. Do not launch multiple copies or reuse one key across many polling tools.

## Xbox account connects but the achievement feed fails

OpenXBL profile lookup and achievement lookup are separate checks. Use **Sync Xbox now**, confirm internet access to `xbl.io`, and retry later. If the provider changed its JSON shape, copy the redacted support summary and open an issue without attaching raw account JSON.

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

## Installer-provided credentials need attention

Setup only validates the local shape; the app performs live checks after launch. If one fails, Guided setup opens with status details. Choose **Save and connect** to retry the encrypted OpenXBL key without re-entering it, paste a replacement if needed, and/or use **Save and test** for Discord.

The one-time installer file contains DPAPI ciphertext, not plaintext. The app truncates and deletes it on import. If it remains after a crash, exit the app and remove `%LOCALAPPDATA%\AchievementRelay\pending-installer-setup.json`, then use Guided setup.

## Desktop shortcut was not created

Re-run `AchievementRelay_Setup.exe` and enable **Create a desktop shortcut**, or create a shortcut from the installed app's Start-menu entry. The manual `Install.ps1` supports `-CreateDesktopShortcut`.

## Duplicate posts

Successful event IDs are retained locally for 90 days, capped at 1,000. Do not delete `processed-events.json` during ordinary troubleshooting. If duplicates persist, include approximate unlock/post times and the redacted support summary in an issue.

## Safe support report

Use **Diagnostics → Copy support summary**. It excludes the API key, webhook URL/token, XUID, and gamertag. Before posting, check it again for private data.

Never attach `settings.json`, `pending-installer-setup.json`, raw provider responses, or screenshots containing credentials. Revoke any secret that was exposed.
