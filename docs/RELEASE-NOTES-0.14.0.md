# Achievement Relay v0.14.0

## TKB Redline

- Black, red and white UI with the shared two-bar TKB identity, larger game artwork, readable progress and an honest current/last-played distinction.
- Collection filters for Steam, Xbox, completed, in-progress and unknown-total games; searchable history and session timelines.
- Presentation Studio previews real Collector Card showcase output at desktop and smaller widths, compact message content and local Signal Strip overlay animations. Steam and Xbox examples are explicitly labelled; previews do not post to Discord.
- Preview-first session recaps with platform-separated counts and suppressed mentions. Nothing sends until you explicitly choose Send. Imported history and completion celebrations do not inflate unlock counts.
- Verified completion shelf, clearer delivery explanations and background/startup guidance. Unknown progress never becomes a fabricated completion percentage.

## Preserved protections

Steam/Xbox monitoring, Xbox PC Game Pass labels, duplicate prevention, uncertain-delivery safeguards, history isolation, settings draft protection and encrypted account sync retain their existing behaviour. The update adds no automatic recap posting and does not reset your settings or delivery records. Existing reduced-motion, quiet-mode and sound controls remain in effect.

## Installation

Use AchievementRelay_Setup.exe or Relay's update prompt after this release is published. Signed AchievementRelay_0.14.0.0_x64.msix and ARM64 packages are included in the release workflow. Minimum supported update version remains v0.4.0.

## Validation boundary

Windows CI checks native UI rendering, application/core contracts, real local overlay animation and installer safety. Installed live-game, multi-monitor/high-DPI, startup permission and upgrade/uninstall smoke checks remain separate; CI screenshots are not evidence of those real-device scenarios.
