# Achievement Relay v0.7.0

## Redline desktop redesign

The approved black-and-crimson Redline design brings a compact navigation rail, provider status tiles, connection controls, and an achievement artwork showcase to Home. Presentation has its own page for inspecting the most recently selected delivered Collector Card.

- Successful Discord deliveries populate a selectable list of the latest eight achievements in the current session.
- The showcase reuses downloaded provider artwork; missing artwork uses Relay branding. No sample unlock is presented as a real delivery.
- The Home overlay switch saves only that preference. Discord delivery remains independent of the Signal Strip overlay.
- Connection management, setup, updates, activity, settings, help and local previews remain available.
- The Collector Card showcase and evidence-based platform labels, including Xbox PC Game Pass where the provider supplies PC evidence, are preserved.

## Safety and privacy

No new network destinations, analytics or account requirements are introduced. The showcase observes only successfully posted, durably recorded events. It cannot cause a failed UI update to retry a Discord post. Session artwork/history is not persisted; existing processed-event deduplication and no-history-flood boundaries are unchanged.

Native dashboard previews run with isolated temporary storage and without starting monitors or the tray. Windows CI renders both desktop and compact layouts for review.

## Updating

This optional feature release retains the official v0.4.0 minimum-supported update baseline and existing certificate-pinned updater. Settings and provider state are preserved.

Use **AchievementRelay_Setup.exe** for the normal installation. Packaged assets include **AchievementRelay_0.7.0.0_x64.msix** and the corresponding ARM64 package. Download only from the official GitHub release.
