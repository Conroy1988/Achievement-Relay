# Troubleshooting

## The sample post fails

1. Open **Settings** and paste a newly copied Discord webhook URL.
2. Select **Test Discord**.
3. Confirm the webhook still exists and points to the intended channel.
4. Confirm your firewall, DNS filter, VPN, or proxy allows HTTPS access to `discord.com`.

Discord responses such as 401 or 404 usually mean the webhook was deleted, rotated, or copied incorrectly. Create a replacement webhook and save it.

## Windows access says unavailable

The notification capability needs MSIX package identity. Install the packaged release; an unpackaged executable launched from `dotnet run` cannot complete real notification capture.

## Windows access says blocked

Open **Guided setup**, select **Open Windows settings**, and re-enable notification access for Achievement Relay. Then select **Grant access** again.

## The Xbox notification never appears

1. Press <kbd>Windows</kbd> + <kbd>G</kbd>.
2. Open Game Bar settings and enable achievement notifications.
3. Check Windows **Settings → System → Notifications** for Xbox and Game Bar.
4. Confirm the game is signed into the expected Xbox account.
5. Wait for Xbox validation; some unlocks are delayed, especially after offline play.

Achievement Relay cannot observe an event that Xbox never surfaces as a Windows notification.

## The Xbox notification appears but nothing posts

1. Confirm the tray icon is present.
2. Open **Dashboard** and confirm Windows Access is **Allowed**, Discord is **Connected**, and Relay is **Monitoring**.
3. Open **Diagnostics** and select **Re-scan current notifications** before clearing Notification Center.
4. Review **Activity** for “ignored”, “duplicate”, “not configured”, or delivery errors.
5. Send a sample achievement to separate capture problems from Discord problems.

## A notification is parsed incorrectly

Open **Diagnostics → Copy support summary**. The summary deliberately excludes notification text and webhook secrets. Open a GitHub issue with that summary, Windows display language, game name, and a manually redacted transcription or screenshot of only the Xbox achievement notification.

Never post your Discord webhook URL. If it is exposed, delete or rotate it in Discord immediately.

## Duplicate posts

Successful achievement fingerprints are retained locally. If duplicates persist, include the approximate unlock times and redacted notification shapes in an issue. Deleting `processed-events.json` resets deduplication and may cause retained notifications to be sent again during a manual re-scan.
