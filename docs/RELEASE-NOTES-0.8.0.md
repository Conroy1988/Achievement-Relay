# Achievement Relay v0.8.0

## Animated Signal Strip overlay

Unlocks now arrive with a crimson sweep, a subtle achievement-artwork pulse, a title reveal, a metallic rarity shimmer and a draining countdown line. Platinum unlocks receive one extra sparkle. The strip stays compact, passive and click-through, then retracts after its five-second hold.

An original one-second, two-note unlock chime is generated locally. Sound defaults to 15% volume and can be disabled independently of animation. The existing bounded queue presents unlocks one at a time without overlapping chimes. Closing or cancelling a strip stops its audio.

In **Settings → In-game achievement overlay**, choose animation, reduced motion, sound and volume. **Test unlock — animation and sound** previews the controls locally without posting to Discord or saving other settings. Reduced motion uses a simple fade; Windows-disabled animations and high-contrast mode suppress decorative motion.

## Preserved safety

Historical baselines remain silent; only already-eligible live events and explicit local previews enter the queue. No additional media downloads, network destinations, telemetry or audio assets from other products are introduced. Sound-device failures cannot block Discord delivery. Saving preferences clears pending presentations so older sound choices cannot linger.

The Redline dashboard, Collector Card showcase and evidence-based Xbox PC Game Pass labels are unchanged. The updater support floor remains v0.4.0. Existing connections, state, overlay opt-out and deduplication history are preserved; new animation and sound preferences default on at 15% volume.

## Installation

Use **AchievementRelay_Setup.exe**. The release also contains **AchievementRelay_0.8.0.0_x64.msix**, the ARM64 package, manual installer ZIP, public publisher certificate and signed update manifest.
