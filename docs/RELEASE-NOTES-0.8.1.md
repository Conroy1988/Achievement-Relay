# Achievement Relay v0.8.1

## Signal Strip overlay animation fix

“Animate unlocks” now controls Relay directly. Windows animation preferences no longer silently turn an enabled animation into a static strip. The crimson sweep, artwork pulse, rarity shimmer and draining countdown run when full animation is selected.

Settings now offers an explicit **Follow Windows animation preference** option and reports the effective animation mode. Reduced motion remains fade-only; disabling animation remains static. Windows high-contrast mode always suppresses decorative motion. Sound and volume remain independent.

Windows verification now clicks the actual Settings **Test unlock** button and checks the real queue and presentation, including unsaved controls and full, static and reduced-motion behavior. The previous forced-effects preview bypass has been removed.

## Preserved safety

No Discord post is sent by Test unlock. Existing connections, preferences, state and historical baselines are preserved. The bounded queue remains one-at-a-time, passive and click-through.

The Redline dashboard, Collector Card showcase and Xbox PC Game Pass labels are unchanged. The updater support floor remains v0.4.0.

## Installation

Use **AchievementRelay_Setup.exe**. The release also contains **AchievementRelay_0.8.1.0_x64.msix**, the ARM64 package, manual installer ZIP, public publisher certificate and signed update manifest.
