# Achievement Relay v0.5.0

v0.5.0 transforms every achievement notification into a full **Collector Card** while keeping the relay's existing privacy, anti-backlog and verified-update protections.

## Collector Cards

Achievement posts now lead with a wide, locally rendered card designed for Discord rather than a small generic embed thumbnail. Each card brings together:

- the game and achievement names;
- the source platform;
- the global unlock percentage when the provider supplies one;
- a dedicated Relay rarity emblem and tier;
- game or achievement artwork when usable artwork is available; and
- a premium black, red and metallic Achievement Relay fallback when it is not.

The fallback is a first-class design, not an error state. Missing, unreachable or unsuitable provider artwork can never leave a blank image or prevent an otherwise valid achievement from reaching Discord.

The attachment includes a concise description, and Discord also retains the important achievement facts as ordinary embed text. The post therefore remains useful across screen readers and clients with different image/attachment-description support.

## Relay rarity tiers

The reported global unlock percentage is now a primary part of the card. Achievement Relay applies one consistent visual scale across Xbox and Steam:

| Tier | Global unlock rate | Card treatment |
| --- | ---: | --- |
| **Bronze** | 25% or more | Bronze round-medal emblem |
| **Silver** | 10% to 24.99% | Silver shield emblem |
| **Gold** | 3% to 9.99% | Gold starburst emblem |
| **Platinum** | Under 3% | Platinum faceted-diamond emblem |
| **Unranked** | Percentage unavailable | Neutral Relay emblem |

Provider percentages are validated before use. A missing, malformed or out-of-range value is shown as **Unranked** rather than being guessed or treated as zero.

## Clearer Xbox platform labels

Xbox posts no longer expose the internal provider name. Achievement Relay uses the strongest reliable platform evidence available for the changed title and can distinguish **Xbox PC**, **Xbox Console** and **Xbox 360**. Play Anywhere titles or provider responses that do not prove one device remain labelled **Xbox** instead of making a false claim.

Steam achievements continue to be labelled **Steam**.

## Preserved reliability and privacy

- Cards are rendered locally and delivered only to the configured Discord webhook.
- Provider text is bounded before layout, and optional remote artwork is treated as untrusted input.
- A card or artwork failure falls back safely without weakening delivery retries or deduplication.
- Xbox and Steam baselines, pending deliveries, encrypted connections, settings and processed-event history are preserved during the update.
- The v0.4.2 cross-device reconciliation and muted updater behavior remain intact.
- Every visible app launch, including the verified updater relaunch, continues to open Home as fixed in v0.4.3.

## Updating

Installed v0.4.0 and newer copies use the existing certificate-pinned GitHub updater. v0.5.0 is an optional feature release and retains `0.4.0` as the minimum supported updater version.

## Release assets

- `AchievementRelay_Setup.exe` — recommended installer and verified updater
- `AchievementRelay_0.5.0.0_x64.msix` — x64 Windows package
- `AchievementRelay_0.5.0.0_arm64.msix` — Arm64 Windows package
- `AchievementRelay_0.5.0.0_installer.zip` — manual installation fallback
- `AchievementRelay_Update.json` and `AchievementRelay_Update.sig` — signed automatic-update contract
- `AchievementRelay.Publisher.cer` — public package certificate
