# Getting started

Achievement Relay needs a Discord webhook for posting to one channel. Steam monitoring is local and needs no API key. Xbox is optional and uses your own OpenXBL API key. Setup can collect the webhook and optional Xbox key during installation, or you can skip that page and add them later in the app.

## 1. Prepare the connections

### Steam

Install and sign in to the normal Windows Steam desktop client. Nothing else is required: do not create a Steam Web API key, paste a Steam64 ID, or share Steam credentials. Achievement Relay detects the active Steam game automatically.

### OpenXBL API key (only if using Xbox)

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
2. If Microsoft Defender SmartScreen appears, confirm the file came from this repository's official release, choose **More info**, then **Run anyway** only if you intended to install Achievement Relay.
3. Approve the one-time administrator prompt to trust the included public **Achievement Relay Open Source** package certificate. It is a project-owned self-signed certificate, not a certificate authority; later official updates reuse the same pinned identity.
4. **Relay Online** by CRNY starts locally at 10% volume. Use the lower-left **Pause music**/**Play music** control at any time; the adjacent SoundCloud button opens the [direct track page](https://soundcloud.com/daniel-conroy-224318319/crny-relay-online) only when selected.
5. On **Connect your relay**, choose one option:
   - **Connect Discord now; add OpenXBL optionally (recommended)**; or
   - **Skip — I will do this later in Setup**.
6. If connecting now, paste the Discord webhook. Paste an OpenXBL key only if you also want Xbox monitoring. Setup validates supplied values; the app performs live checks on first launch.
7. On **Player options**, toggle **Create a desktop shortcut** as preferred.
8. Select **Install**.

The installer never places either secret on a command line or in its log. It uses a one-time DPAPI-encrypted file under `%USERPROFILE%\.achievement-relay` for the signed-in Windows user, clears the installer fields, and launches the app. The app saves fresh encrypted settings before it truncates and deletes that handoff. Steam creates no secret handoff.

## 3. First launch

If credentials were supplied in Setup, Achievement Relay automatically:

1. decrypts the one-time installer handoff;
2. protects and durably saves the webhook and any optional OpenXBL key in normal settings with current-user DPAPI;
3. deletes the one-time handoff;
4. verifies the optional Xbox account and achievement feed through OpenXBL;
5. sends one green connection-test embed to Discord;
6. starts Steam monitoring immediately and Xbox monitoring when configured; and
7. creates a silent baseline for each source before any real achievement can be posted.

If Discord works but optional Xbox verification fails, Steam can still start immediately and the saved Xbox key can be retried later. Stored secret fields show the real saved value in masked form; use **Reveal Key** or **Reveal Webhook** to inspect it, then select the retry button or type a replacement. Nothing is posted except the clearly disclosed Discord connection test and later genuine unlocks.

## 4. In-app setup when skipped

1. In step 1, choose Xbox, Steam, or both. Steam needs no key.
2. In step 2, connect Xbox through OpenXBL if selected, or skip Xbox to continue with Steam only.
3. In step 3, paste the Discord webhook, choose **Save and send a test**, and check the selected Discord channel.
4. In step 4, choose the optional display name and Windows startup preferences, review the ready check, then select **Finish setup**.

You can close the window after setup. Achievement Relay continues in the Windows notification area; right-click the tray icon to reopen or exit.

## 5. Test a real Steam achievement

1. Leave Achievement Relay and Steam running.
2. Start a Steam game. Home should change from **Steam: Ready** to **Steam: Monitoring**.
3. The first complete snapshot for that Steam account and game becomes a silent baseline. No historical achievements are posted; only Steam's direct completed-achievement callback can prove a live unlock during the launch-to-baseline window.
4. Unlock a different achievement after the baseline appears in Activity.
5. Check Discord. A local unlock is normally noticed within a few seconds.

If Achievement Relay was closed when an achievement unlocked, that identity is silently added to the baseline the next time the game is monitored. Achievement Relay posts only a live locked-to-unlocked transition it directly observed or Steam's completed-achievement callback received during the current helper session. Timestamps are display metadata and never authorize a post. This is the anti-backlog safety boundary.

## 6. Test a real Xbox achievement

1. Leave Achievement Relay running.
2. Unlock an Xbox network achievement in a PC game.
3. Wait up to about one minute, plus any delay while Xbox syncs the unlock.
4. Check the configured Discord channel.

A Windows Notification Center toast is not required. The Xbox Game Bar overlay may be the only local pop-up and the relay can still work because the app checks the account feed.

If nothing arrives:

1. open **Help & support**;
2. select **Sync Xbox now**;
3. read **Last sync error** and **Activity**; and
4. continue with [Troubleshooting](docs/TROUBLESHOOTING.md), or select **Join Discord** for [Community & Support](https://discord.gg/3ZdXhYjgDm).

## Upgrading from 0.1.x

Version 0.1.x depended on Windows Notification Center and cannot detect a Game Bar-only overlay. Version 0.2 preserves the encrypted Discord webhook and preferences, but deliberately reopens Setup so the user can add an OpenXBL key. The first account check creates a lightweight baseline; it neither dumps historical achievements into Discord nor downloads every old title immediately. Exact historical identities are filled in silently under the 15-minute background schedule while live achievement changes remain prioritised.

## Upgrading from 0.2.x

Version 0.4 preserves the Xbox cursor, processed-event ledger, encrypted OpenXBL key, encrypted Discord webhook, and preferences. Steam monitoring is enabled by default and waits locally for the next Steam game. Each Steam account/game pair receives its own first complete silent baseline, so upgrading cannot dump the Steam library's achievement history into Discord.

## Moving from a pre-official 0.3.x test

Install v0.4.0 once from the [official GitHub release](https://github.com/Conroy1988/Achievement-Relay/releases/tag/v0.4.0). The isolated 0.3.x updater exercise used a deliberately temporary certificate whose private key was destroyed after the test. That build therefore cannot authenticate the new persistent publisher identity automatically.

This one-time manual signing transition preserves the encrypted Discord webhook, OpenXBL key, source selection, Xbox and Steam baselines, event ledger, pending deliveries, startup preference, and desktop-shortcut choice. Do not uninstall or remove local data first.

## Automatic updates from 0.4 onward

Achievement Relay checks the latest stable release on the official GitHub repository at startup and about every six hours. A normal newer release is optional. A release is required only when its reviewed, publisher-signed manifest raises `minimumSupportedVersion`; once that policy is successfully authenticated, Xbox and Steam monitoring pause until the update is installed. A failed, unsigned, tampered, or offline check never invents a requirement.

Select **Update now** on Home or **Help & support**. The app downloads the exact GitHub release asset to `%LOCALAPPDATA%\AchievementRelay\Updates`, verifies its declared size, SHA-256, signed product/package versions, matching executable product/file versions, Windows Authenticode trust, and the publisher-certificate fingerprint pinned into the running build, then opens the branded updater. Because first installation trusted that same persistent certificate, normal later updates do not repeat the certificate-import prompt. The updater uses the same CRNY music at 10% volume with Play/Pause and the direct SoundCloud link. It keeps encrypted connections, settings, provider baselines, pending deliveries, startup behavior, and the existing desktop-shortcut choice. Cancelling before installation leaves the running app untouched.

## What cannot be automated

| User action | Reason |
|---|---|
| Create/connect an OpenXBL account | OpenXBL owns its account, Xbox authorization, terms, and API-key lifecycle. |
| Create a Discord webhook | A Discord member with permission must choose the server and channel. |
| Sign in to Steam and launch the game | The local Steam client owns the account session and chooses the active app context. Achievement Relay never asks for Steam credentials. |
| Approve Windows installation or SmartScreen prompts | Windows controls installation and reputation prompts. Verify that the installer came from the official release before continuing. |
| Earn and sync an achievement | The game and Xbox service decide when the unlock is awarded and visible. |

After those choices, polling, baseline protection, filtering, deduplication, formatting, retry, secure storage, startup, and Discord posting are automatic.
