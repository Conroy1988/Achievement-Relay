# Privacy policy

Effective: 15 August 2026

Achievement Relay is a local, open-source Windows application. It has no analytics, advertising, account system, cloud database, telemetry, or developer-operated relay service.

## OpenXBL account access

Version 0.2 uses [OpenXBL](https://xbl.io), an independent and unofficial Xbox API provider. The user supplies a personal OpenXBL API key. Achievement Relay sends that key only to OpenXBL's documented `https://api.xbl.io/api/v2/` service in an `X-Authorization` HTTPS header to request:

- the connected Xbox profile, including XUID and gamertag; and
- the account's achievement feed, including achievement/title identifiers, names, descriptions, Gamerscore, rarity, artwork URLs, status, and unlock timestamps when available.

OpenXBL processes those requests under the user's relationship with OpenXBL. Review OpenXBL's privacy policy and terms before connecting an account. Achievement Relay cannot control OpenXBL's retention, availability, or service changes.

The app does not ask for or store the Xbox/Microsoft password, Microsoft authentication token, or Discord account credentials. It does not read Windows notifications.

## Data sent to Discord

For each new achievement, Achievement Relay may send the following to the user-selected Discord webhook:

- achievement name and description;
- game/title name;
- Gamerscore and rare status;
- unlock timestamp;
- player display name; and
- an Xbox/OpenXBL-provided HTTP image URL.

Discord receives this data under the user's relationship with Discord. The app disables Discord mention parsing so an achievement title cannot ping `@everyone` or a role.

## Local storage

Achievement Relay stores data under `%LOCALAPPDATA%\AchievementRelay`:

- `settings.json`: preferences, setup state, XUID, gamertag, and current-user DPAPI ciphertext for the OpenXBL API key and Discord webhook;
- `xbox-sync-state.json`: account identifier, baseline timestamp, and last successful poll timestamp;
- `processed-events.json`: deterministic achievement identifiers and processed timestamps, capped at 1,000 entries and 90 days; and
- `achievement-relay.log`: a size-bounded operational log with status/errors and achievement names involved in delivery.

The XUID and gamertag are not secret credentials and are stored as ordinary local settings, but the copied support summary deliberately omits both. The app never intentionally writes the plaintext API key, webhook URL/token, Xbox password, Microsoft token, or Discord credentials to its log.

## Optional installer setup

If the user selects **Connect OpenXBL and Discord now** in `AchievementRelay_Setup.exe`:

1. the masked fields exist in installer process memory;
2. Setup passes them to a child protection step using short-lived process environment variables—not command-line arguments;
3. that step encrypts each value with Windows DPAPI scoped to the current user before writing `%USERPROFILE%\.achievement-relay\pending-installer-setup.json`;
4. Setup clears the environment variables and input fields;
5. the app reads the one-time encrypted file and durably saves fresh DPAPI-protected settings;
6. the app truncates and deletes the handoff; and
7. the app live-tests the connections.

If installation or first launch is interrupted before durable storage, the one-time file can remain, but its values are still encrypted for that Windows user. The next launch retries the import. Selecting **Skip — I will do this later** creates no credential handoff.

## Network access

The app makes outbound HTTPS requests to:

- `xbl.io` for account and achievement polling; and
- the validated Discord-owned webhook host for connection tests and achievement delivery.

Documentation, GitHub, OpenXBL, Discord help, and Ko-fi links open in the default browser only when selected. Achievement Relay does not send data to Ko-fi.

## Retention and deletion

Disconnecting the Xbox account or removing the Discord webhook deletes the corresponding saved ciphertext from settings. Uninstall the app and run `Uninstall.ps1 -RemoveLocalData` to remove durable state and any remaining handoff. Manual cleanup requires deleting both `%LOCALAPPDATA%\AchievementRelay` and `%USERPROFILE%\.achievement-relay`.

Revoking the OpenXBL API key stops future account requests. Deleting/rotating the Discord webhook immediately prevents future use of that URL.

## Changes and contact

Material policy changes are recorded in repository history and release notes. Privacy reports can be opened in the project's GitHub tracker, but never include API keys, webhook URLs/tokens, XUIDs, gamertags, or other private data.
