# Privacy policy

Effective: 16 August 2026

Achievement Relay is a local, open-source Windows application. It has no analytics, advertising, account system, cloud database, telemetry, or developer-operated relay service.

## OpenXBL account access

Xbox support uses [OpenXBL](https://xbl.io), an independent and unofficial Xbox API provider. The user supplies a personal OpenXBL API key only when enabling Xbox. Achievement Relay sends that key only to OpenXBL's `https://api.xbl.io/` service in an `X-Authorization` HTTPS header to request:

- the connected Xbox profile, including XUID and gamertag; and
- the account's achievement feed, including achievement/title identifiers, names, descriptions, Gamerscore, rarity, artwork URLs, status, and unlock timestamps when available.

OpenXBL processes those requests under the user's relationship with OpenXBL. Review OpenXBL's privacy policy and terms before connecting an account. Achievement Relay cannot control OpenXBL's retention, availability, or service changes.

The app does not ask for or store the Xbox/Microsoft password, Microsoft authentication token, or Discord account credentials. It does not read Windows notifications.

## Local Steam access

Steam support is keyless and read-only. Achievement Relay reads the signed-in Windows user's local Steam install path, library/manifests, active App ID, running executable paths, current Steam account ID/player name, and the active game's achievement identifiers, display metadata, unlock state/time, and optional icon through the local Steamworks client. It does not ask for or store a Steam password, browser cookie, Web API key, OAuth token, or other login credential.

Only after a new unlock, the app may request public global achievement percentages from `api.steampowered.com` for that App ID. The result is cached for the running process and contains aggregate percentages, not personal history.

## Data sent to Discord

For each new achievement, Achievement Relay may send the following to the user-selected Discord webhook:

- achievement name and description;
- game/title name;
- Gamerscore when supplied and rarity status/percentage when available;
- unlock timestamp;
- player display name;
- an Xbox/OpenXBL-provided HTTP image URL or a locally read Steam achievement icon uploaded as a PNG attachment; and
- the source platform (Xbox or Steam); and
- the public Achievement Relay GitHub URL rendered as a **Get the relay** link.

Discord receives this data under the user's relationship with Discord. The app disables Discord mention parsing so an achievement title cannot ping `@everyone` or a role.

## Local storage

Achievement Relay stores data under `%LOCALAPPDATA%\AchievementRelay`:

- `settings.json`: preferences, setup state, XUID, gamertag, and current-user DPAPI ciphertext for the OpenXBL API key and Discord webhook;
- `xbox-sync-state.json`: account identifier, baseline/poll/background timestamps, per-title achievement counts, Gamerscore and stable achievement IDs, plus queued title identifiers, names, counts and last-played/observation times needed to pace unfinished identity baselines across restarts;
- `steam-sync-state.json`: Steam account ID and, for each monitored App ID, game name, baseline/observation times, the monotonic set of unlocked achievement API names, and any live transition awaiting Discord delivery;
- `processed-events.json`: deterministic achievement identifiers and processed timestamps, capped at 1,000 entries and 90 days; and
- `achievement-relay.log`: a size-bounded operational log with status/errors and achievement names involved in delivery.

The XUID, gamertag, Steam account ID, and Steam player name are not bearer credentials, but the copied support summary deliberately omits them. The app never intentionally writes the plaintext API key, webhook URL/token, Xbox/Steam password, Microsoft/Steam token, or Discord credentials to its log.

## Optional installer setup

If the user selects **Connect Discord now; add OpenXBL optionally** in `AchievementRelay_Setup.exe`:

1. the masked fields exist in installer process memory;
2. Setup passes them to a child protection step using short-lived process environment variables—not command-line arguments;
3. that step encrypts each value with Windows DPAPI scoped to the current user before writing `%USERPROFILE%\.achievement-relay\pending-installer-setup.json`;
4. Setup clears the environment variables and input fields;
5. the app reads the one-time encrypted file and durably saves fresh DPAPI-protected settings;
6. the app truncates and deletes the handoff; and
7. the app live-tests Discord and any supplied Xbox connection. Steam creates no secret or installer handoff.

If installation or first launch is interrupted before durable storage, the one-time file can remain, but its values are still encrypted for that Windows user. The next launch retries the import. Selecting **Skip — I will do this later** creates no credential handoff.

The installer contains the original CRNY track **Relay Online**. Setup extracts it only to its automatically cleaned temporary directory and first plays it through a private Windows Media Player instance fixed at 10% volume, with an independently volume-limited Windows MCI fallback. Neither path changes the user's Windows master volume, and playback is not started if a safe per-player volume cannot be enforced. Setup provides Play/Pause controls. The track is not sent over the network or retained as a separately installed file. The SoundCloud profile opens in the default browser only if the user selects that button.

## Network access

The app makes outbound HTTPS requests to:

- `api.xbl.io` for account and achievement polling; and
- `api.steampowered.com` for a public, keyless global-rarity lookup only after a new Steam unlock;
- the validated Discord-owned webhook host for connection tests and achievement delivery.

Documentation, GitHub, OpenXBL, Discord help, Ko-fi, and SoundCloud links open in the default browser only when selected. Achievement Relay does not send data to Ko-fi or SoundCloud unless the user chooses to open the corresponding website in their browser.

## Retention and deletion

Disconnecting Xbox or removing Discord deletes the corresponding saved ciphertext. Disabling Steam stops local monitoring but retains its baseline to prevent replay if re-enabled. Uninstall the app and run `Uninstall.ps1 -RemoveLocalData` to remove all provider state and any remaining handoff. Manual cleanup requires deleting both `%LOCALAPPDATA%\AchievementRelay` and `%USERPROFILE%\.achievement-relay`.

Revoking the OpenXBL API key stops future account requests. Deleting/rotating the Discord webhook immediately prevents future use of that URL.

## Changes and contact

Material policy changes are recorded in repository history and release notes. Privacy reports can be opened in the project's GitHub tracker, but never include API keys, webhook URLs/tokens, XUIDs, gamertags, or other private data.
