# Achievement Relay v0.6.0

v0.6.0 adds the compact **Signal Strip**: a polished local achievement banner that celebrates a genuine live unlock without taking control away from the game.

## Signal Strip overlay

Each newly proven Xbox or Steam achievement can now appear in a slim banner at the top centre of the active display. The five-second presentation combines:

- achievement artwork, or the premium Achievement Relay fallback when no usable image exists;
- the achievement and game names;
- the exact global player unlock percentage when available;
- the matching Bronze, Silver, Gold, Platinum or Unranked rarity emblem;
- the evidence-based platform label; and
- the Gamerscore reward when supplied by the provider.

The strip slides into view, holds long enough to read and retracts cleanly. Consecutive achievements enter a bounded first-in, first-out queue and are shown one at a time instead of stacking over gameplay.

## Passive by design

The Signal Strip is intentionally a notification, not another game overlay platform:

- it does not activate or take keyboard focus;
- mouse input passes through to the game beneath it;
- it has no achievement sound; and
- it dismisses itself automatically after five seconds.

The overlay is enabled by default for new and upgraded installations. **Settings → Achievement overlay** can disable it without disabling Xbox monitoring, Steam monitoring or Discord posts. An explicit opt-out survives repairs and later updates.

## Honest rarity and platform data

The local strip uses the same validation and presentation language as v0.5.0 Collector Cards:

| Tier | Global unlock rate |
| --- | ---: |
| **Bronze** | 25% or more |
| **Silver** | 10% to 24.99% |
| **Gold** | 3% to 9.99% |
| **Platinum** | Under 3% |
| **Unranked** | Percentage unavailable |

A missing or invalid percentage remains **Unranked** rather than becoming a false `0%` or Platinum result. Xbox PC, Xbox Console and Xbox 360 labels appear only when the existing provider evidence proves them; ambiguous Xbox titles stay labelled **Xbox**.

## Preserved safety and privacy

- Only achievements that pass the existing live-unlock eligibility boundary can enter the overlay queue.
- First baselines, startup reconciliation, offline history and unproven provider changes remain silent.
- Overlay composition happens entirely on the PC and adds no telemetry, screen capture, hosted account or new network destination.
- Discord delivery remains independent: disabling the local overlay does not disable Collector Cards or change delivery retries.
- Encrypted connections, provider state, processed-event history, cross-device reconciliation, updater silence and the certificate-pinned automatic-update chain are preserved.

## Updating

Installed v0.4.0 and newer copies use the existing certificate-pinned GitHub updater. v0.6.0 is an optional feature release and retains `0.4.0` as the minimum supported updater version.

## Release assets

- `AchievementRelay_Setup.exe` — recommended installer and verified updater
- `AchievementRelay_0.6.0.0_x64.msix` — x64 Windows package
- `AchievementRelay_0.6.0.0_arm64.msix` — Arm64 Windows package
- `AchievementRelay_0.6.0.0_installer.zip` — manual installation fallback
- `AchievementRelay_Update.json` and `AchievementRelay_Update.sig` — signed automatic-update contract
- `AchievementRelay.Publisher.cer` — public package certificate
