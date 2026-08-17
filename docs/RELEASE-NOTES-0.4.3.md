# Achievement Relay v0.4.3

v0.4.3 is a focused startup-navigation hotfix.

## Post-update relaunches now open Home

After the verified updater installed v0.4.2, an already-configured app could relaunch with no selected content tab. The dark application shell appeared, but the main content area was empty until the user selected a menu item.

This release makes startup navigation deterministic:

- **Home** is selected in the interface definition before the first frame is rendered.
- The window constructor explicitly establishes Home as the default route.
- Every normal visible launch—including the updater relaunch—opens Home.
- A loaded-window fallback restores Home if Windows ever reports that no content tab is selected.
- Guided Setup and required-update routes can still intentionally replace Home when they are needed.

A repository regression guard now requires all four protections, preventing a future hidden-tab styling change from restoring the blank startup surface.

## Preserved behavior

The v0.4.2 muted-updater behavior, fixed 10% opt-in soundtrack controls, Xbox cross-device reconciliation, Steam monitoring, encrypted settings, provider baselines, processed identities and automatic-update trust chain are unchanged.

## Updating

Installed v0.4.0, v0.4.1 and v0.4.2 copies use the existing certificate-pinned GitHub updater. On launch, Achievement Relay can automatically download, verify and open this optional hotfix.

## Release assets

- `AchievementRelay_Setup.exe` — recommended installer and verified updater
- `AchievementRelay_0.4.3.0_x64.msix` — x64 Windows package
- `AchievementRelay_0.4.3.0_arm64.msix` — Arm64 Windows package
- `AchievementRelay_0.4.3.0_installer.zip` — manual installation fallback
- `AchievementRelay_Update.json` and `AchievementRelay_Update.sig` — signed automatic-update contract
- `AchievementRelay.Publisher.cer` — public package certificate
