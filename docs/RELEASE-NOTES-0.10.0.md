# Achievement Relay v0.10.0

## Showcase, sound and a quieter way to play

- Now-playing home panel with Steam detection, clearly labelled last-observed Xbox game, verified progress and local-session unlock count.
- Cinematic Signal Strip overlay beam expansion, rare-unlock particles, larger completion burst and smooth retraction; reduced-motion and high-contrast preferences remain authoritative.
- Sound Studio: three original local packs, standard/rare choices, dedicated volume and previews. The master sound switch still applies.
- Visual screen editor uses an actual rendered strip, resize controls, edge/centre snapping and fixed-screen selection. Borderless gaming guidance explains exclusive-fullscreen limitations; no game injection or anti-cheat bypass.
- Local game library, closest-to-completion shelf, optional historical achievement capture, trophy pins, rarest-first browsing and PNG poster export.
- History import is opt-in and occurs only when monitoring naturally receives a validated snapshot. It is not a full-account scan: up to 100 observed games, 300 imported achievements per game and 3,000 imported achievements overall. Imported entries cannot be sent through live delivery or automatic overlays.
- Local session timeline, delivery-transition timeline, private-safe diagnostic export, tray shortcuts and temporary mute/hide/hold controls. Holding is capped at eight alerts; Discord continues normally.
- In-app release tour with local samples; bounded artwork caching and process/cache metrics in Health.

## Preserved

Collector Card showcase, Xbox PC Game Pass classification, existing credentials, baselines, retries and the v0.4.0 minimum-supported update baseline are unchanged. The existing Windows-share cross-PC option is unchanged; no hosted pairing service has been added.

## Installation

Use `AchievementRelay_Setup.exe`. The release also provides `AchievementRelay_0.10.0.0_x64.msix`, its ARM64 counterpart, the fallback installer ZIP, publisher certificate and signed update manifest. Install the official release rather than a temporary CI package.

## Practical limits

Exclusive fullscreen and actual multi-monitor/game combinations still require device testing. Unknown platform totals remain unknown; historical snapshots may have incomplete rarity/artwork. Quiet preferences are temporary and reset when Relay exits. History import does not post or automatically replay old unlocks. Exported posters may contain achievement and player information from the selected live event; support reports deliberately exclude it.
