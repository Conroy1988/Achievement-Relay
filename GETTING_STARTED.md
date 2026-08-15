# Getting started

Achievement Relay needs two private values: an OpenXBL API key for reading the connected Xbox achievement feed and a Discord webhook URL for posting to one channel. Setup can collect both during installation, or you can skip that page and add them later in the app.

## 1. Prepare the connections

### OpenXBL API key

1. Open [OpenXBL Profile](https://xbl.io/profile).
2. Create/sign in to your OpenXBL account and follow OpenXBL's prompts to connect the Xbox profile.
3. Create or copy your personal API key.
4. Treat the key like a password. Do not paste it into GitHub issues, screenshots, chat, or logs.

OpenXBL is an independent, unofficial Xbox API provider. Review its terms, privacy policy, and current [request allowance](https://xbl.io/pricing) before connecting an account.

### Discord webhook

You need **Manage Webhooks** permission in the destination Discord server.

1. In Discord, open **Server Settings → Integrations → Webhooks**.
2. Select **New Webhook**, choose a channel, and select **Copy Webhook URL**.
3. Treat the URL like a password: anyone who has it can post through that webhook.

Discord's [official webhook guide](https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks) has screenshots for each Discord client.

## 2. Run the installer

1. Download `AchievementRelay_Setup.exe` from the [latest GitHub Release](https://github.com/Conroy1988/Achievement-Relay/releases/latest).
2. If Microsoft Defender SmartScreen appears for this beta, confirm the file came from the official repository, choose **More info**, then **Run anyway**.
3. On **Connect your relay**, choose one option:
   - **Connect OpenXBL and Discord now (recommended)**; or
   - **Skip — I will do this later in Guided setup**.
4. If connecting now, paste the API key and webhook into the masked fields. Setup validates their shape; the app performs the live checks on first launch.
5. On **Player options**, toggle **Create a desktop shortcut** as preferred.
6. Select **Install**. A development-signed beta may request administrator approval once to trust its public package certificate.

The installer never places either secret on a command line or in its log. It uses a one-time DPAPI-encrypted file under `%USERPROFILE%\.achievement-relay` for the signed-in Windows user, clears the installer fields, and launches the app. The app saves fresh encrypted settings before it truncates and deletes that handoff.

## 3. First launch

If credentials were supplied in Setup, Achievement Relay automatically:

1. decrypts the one-time installer handoff;
2. protects and durably saves the API key and webhook in normal settings with current-user DPAPI;
3. deletes the one-time handoff;
4. verifies the Xbox account and achievement feed through OpenXBL;
5. sends one green connection-test embed to Discord;
6. creates a baseline so old achievements are not reposted; and
7. starts one-minute monitoring if both checks succeed.

If either check fails, the values that passed local validation remain stored encrypted and the app opens **Guided setup** with a useful status. Stored secret fields show the real saved value in masked form; use **Reveal Key** or **Reveal Webhook** to inspect it, then select the retry button or type a replacement. Nothing is silently posted except the clearly disclosed Discord connection test.

## 4. Guided setup when skipped

1. In step 1, paste the OpenXBL API key and choose **Save and connect**.
2. Confirm the connected gamertag. Earlier achievements are baselined and are not sent to Discord.
3. In step 2, paste the Discord webhook and choose **Save and test**.
4. Check the selected Discord channel for the connection message.
5. In step 3, choose the player display name, Windows startup, and tray-start preferences.
6. Select **Finish setup**.

You can close the window after setup. Achievement Relay continues in the Windows notification area; right-click the tray icon to reopen or exit.

## 5. Test a real achievement

1. Leave Achievement Relay running.
2. Unlock an Xbox network achievement in a PC game.
3. Wait up to about one minute, plus any delay while Xbox syncs the unlock.
4. Check the configured Discord channel.

A Windows Notification Center toast is not required. The Xbox Game Bar overlay may be the only local pop-up and the relay can still work because version 0.2 checks the account feed.

If nothing arrives:

1. open **Diagnostics**;
2. select **Sync Xbox now**;
3. read **Last sync error** and **Activity**; and
4. continue with [Troubleshooting](docs/TROUBLESHOOTING.md).

## Upgrading from 0.1.x

Version 0.1.x depended on Windows Notification Center and cannot detect a Game Bar-only overlay. Version 0.2 preserves the encrypted Discord webhook and preferences, but deliberately reopens Guided setup so the user can add an OpenXBL key. The first account check creates a fresh baseline; it does not dump historical achievements into Discord.

## What cannot be automated

| User action | Reason |
|---|---|
| Create/connect an OpenXBL account | OpenXBL owns its account, Xbox authorization, terms, and API-key lifecycle. |
| Create a Discord webhook | A Discord member with permission must choose the server and channel. |
| Approve a development certificate | Windows requires administrator consent for an untrusted beta signing certificate. Production signing removes this step. |
| Earn and sync an achievement | The game and Xbox service decide when the unlock is awarded and visible. |

After those choices, polling, baseline protection, filtering, deduplication, formatting, retry, secure storage, startup, and Discord posting are automatic.
