# Privacy policy

Effective: 14 August 2026

Achievement Relay is a local, open-source Windows application. It has no analytics, advertising, account system, cloud database, or developer-operated relay service.

## Windows notification access

Achievement Relay asks Windows for the **User Notification Listener** capability. Windows presents broad wording because the API can provide notifications from other applications.

After access is granted, Achievement Relay checks each notification's source package/display metadata. It reads text elements only when that source matches a known Xbox or Game Bar component. It discards all other senders before their notification text enters the application. Xbox notification text that does not resemble an achievement is not sent to Discord.

Access can be revoked through Windows Settings at any time.

## Data sent to Discord

For a detected achievement, the app may send these values to the Discord webhook selected by the user:

- achievement name;
- description when available;
- game or title name when available;
- Gamerscore and rare status when available;
- unlock timestamp;
- optional player display name entered by the user; and
- a valid Xbox-provided HTTP image URL when available.

Discord receives this data under the user's relationship with Discord. Review Discord's privacy terms before configuring a webhook. No achievement data is sent anywhere else by the application.

## Local storage

Achievement Relay stores the following under `%LOCALAPPDATA%\AchievementRelay`:

- preferences and setup state;
- the Discord webhook URL encrypted with Windows Data Protection API for the current user;
- achievement fingerprints and timestamps used to prevent duplicate posts, capped at 1,000 entries and 90 days; and
- a size-bounded operational log containing statuses, errors, and the names of Xbox achievements the app detected or attempted to post.

The app does not intentionally write the plaintext webhook, its token, unrelated notification text, Xbox credentials, or Discord credentials to logs.

## Retention and deletion

Users can uninstall the app and remove all local state by running `Uninstall.ps1 -RemoveLocalData`, or by deleting `%LOCALAPPDATA%\AchievementRelay` after exiting the app. Deleting a webhook in Discord immediately prevents further use of that URL.

## Network access

The app makes outbound HTTPS requests only to the configured Discord webhook host during connection tests and achievement delivery. Documentation and GitHub links open in the user's default browser only when selected.

## Changes

Material changes to this policy will be recorded in the repository history and release notes. Questions and privacy reports can be opened in the project's GitHub issue tracker, but webhook URLs and other secrets must never be included.
