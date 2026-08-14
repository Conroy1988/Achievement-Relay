# Getting started

Achievement Relay takes about two minutes to configure. It performs every step Windows and Discord allow an app to automate; Windows notification consent and Discord webhook creation remain deliberate user actions.

## 1. Install the app

1. Open the latest GitHub Release.
2. Download `AchievementRelay_<version>_installer.zip`.
3. Right-click the downloaded ZIP, select **Properties**, select **Unblock** if Windows shows that option, and then select **OK**.
4. Extract the ZIP to a normal folder.
5. Right-click `Install.ps1` and select **Run with PowerShell**. For a development-signed alpha, approve the one administrator prompt used to trust its public package certificate.
6. Achievement Relay opens to **Guided setup**.

The alpha installer uses a project development certificate unless the release is production-signed. [Installation details](docs/INSTALL.md) explain exactly what the script changes.

## 2. Grant notification access

1. Read step 1 in **Guided setup**.
2. Select **Grant access**.
3. Accept the Windows permission prompt.

Windows requires this consent because the notification-listener API can technically access Notification Center. Achievement Relay checks the sender first and only reads content from known Xbox components.

If the prompt does not appear, confirm you installed the MSIX build. The notification listener is unavailable to the unpackaged development executable.

## 3. Enable achievement notifications

1. In step 2, select **Open Windows settings**.
2. Make sure notifications are enabled globally and for **Xbox**, **Xbox Game Bar**, and/or **Game Bar** where listed.
3. Press <kbd>Windows</kbd> + <kbd>G</kbd> to open Game Bar.
4. Open **Settings**, find **Notifications**, and enable achievement unlock notifications.

The notification can be quiet, but it must be created and reach Notification Center. Xbox may delay an unlock while its service validates the achievement.

## 4. Create the Discord webhook

You need **Manage Webhooks** permission in the Discord server.

1. Open Discord and choose the destination server.
2. Open **Server Settings → Integrations → Webhooks**.
3. Select **New Webhook** or **Create Webhook**.
4. Choose the channel and a recognizable webhook name.
5. Select **Copy Webhook URL**.
6. Return to Achievement Relay and paste the URL into step 3.
7. Select **Save and test**.
8. Check the selected Discord channel for the green connection message.

Treat the webhook URL like a password: anyone who has it can post through that webhook. Achievement Relay encrypts it for your current Windows user and never writes it to the activity log.

## 5. Finish setup

1. Optionally enter your gamertag or another display name. It is shown only in achievement posts.
2. Choose whether Achievement Relay should start with Windows.
3. Choose whether startup should remain quiet in the notification area.
4. Select **Finish setup**.
5. Optionally select **Send sample achievement** to preview the Discord embed.

You can close the window after setup. The tray icon remains active; right-click it to reopen or exit.

## 6. Test a real unlock

1. Leave Achievement Relay running in the notification area.
2. Launch an Xbox-enabled PC game.
3. Unlock an achievement.
4. Confirm the Xbox notification appears in Windows.
5. Check the Discord channel.

If the notification appears but Discord receives nothing, open **Diagnostics**, select **Re-scan current notifications**, and review **Activity**. Continue with [Troubleshooting](docs/TROUBLESHOOTING.md).

## What the app cannot automate

| Step | Why user action is required |
|---|---|
| Grant notification access | Windows deliberately requires explicit consent from the signed-in user. |
| Enable Xbox/Game Bar notifications | Windows and Xbox own these settings. The app opens the relevant UI but does not override preferences. |
| Create/copy a Discord webhook | Discord requires a server member with permission to choose the server and channel. |
| Unlock the achievement | The game and Xbox service decide when an achievement is earned and validated. |

Everything after those choices—capture, classification, parsing, deduplication, formatting, retry, secure storage, startup, and posting—is automatic.
