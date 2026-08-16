# Achievement Relay v0.4.1

v0.4.1 is a focused readability and accessibility update for the official v0.4 release channel.

## Fixed

- Removed the native light `TabControl` content background that could turn the main canvas white under Windows light mode.
- Fixed inherited system-black text across Home, Activity, Settings, Help, setup cards and activity lists.
- Kept every app-owned surface consistently near-black with raised dark cards, warm-white primary text and readable secondary text.
- Added separate accessible red text and darker hover tokens instead of using the primary brand red for small labels.
- Made disabled update actions, hover states, input borders, card boundaries and keyboard focus clearly visible.
- Raised every explicit interface label to at least 11 device-independent pixels.
- Added UI Automation names for activity lists, setup/update progress and free-text settings, plus polite announcements for important live statuses.
- Added repository-enforced WCAG contrast checks so a future palette edit cannot reintroduce these failures unnoticed.

The measured normal-text combinations are at least 4.5:1; meaningful card and control boundaries are at least 3:1. The complete audit is recorded in [Accessibility](ACCESSIBILITY.md).

## Updating

An installed v0.4.0 copy checks the official GitHub release automatically. The verified v0.4.1 updater preserves encrypted connections, provider baselines, activity history, pending deliveries, startup behavior and desktop-shortcut choice.

For a manual install or repair, download `AchievementRelay_Setup.exe`. Setup selects x64 or Arm64 automatically and reuses the same persistent publisher identity established by v0.4.0.

## Release assets

- `AchievementRelay_Setup.exe` — recommended installer and updater
- `AchievementRelay_0.4.1.0_x64.msix` — x64 Windows package
- `AchievementRelay_0.4.1.0_arm64.msix` — Arm64 Windows package
- `AchievementRelay_0.4.1.0_installer.zip` — manual installation fallback
- `AchievementRelay_Update.json` and `AchievementRelay_Update.sig` — signed automatic-update contract
- `AchievementRelay.Publisher.cer` — public package certificate
