# Whole-app usability review

This records the scope of the 16-part usability checklist. “Retained” means the existing flow is kept; it does not imply a new live integration test. This branch is a usability implementation, not an official release.

| Area | Changes and retained behaviour | Verification still required |
| --- | --- | --- |
| Structure and navigation | Sidebar Account entry, explained screen purposes, direct detailed-presentation route, scrollable navigation. Setup is the guided connection flow; Settings is the editing flow. Existing navigation preserves page content. | Walkthrough with a new user; decide whether the separate Companion window should eventually become embedded navigation. |
| First-time setup | Existing four-step platform/connection/review flow retained, with skip/back and silent-baseline guidance. Corrected Home's claim that a merely selected platform was ready. Added explicit login-versus-posting explanation in Help and Account. | Fresh Steam-only, Xbox-only and combined installation with real accounts. |
| Home | Removed duplicated connection badges; moved the primary action to the main status area. Ready action opens achievements instead of sending a test. Account state and sync status are visible. Narrow Home stacks its columns. Imported history no longer counts as current-session unlocks. | Idle, paused, retrying and active-game walkthrough on a physical device. |
| Connections | Preserved masked credentials, validation, test and disconnect actions. Xbox action now says “Verify and save key”; save guidance distinguishes immediate connection actions from preference saving. Corrected multiple-PC guidance. | Expired/invalid credentials and replacing a real account without losing unrelated preferences. |
| Account sync | Guided sign-in, recovery and sync; state-gated buttons; browser sign-in cancellation; backup acknowledgement and close reminder; account display label; last-sync timestamp survives restart. Specific safe error guidance. Existing encryption, ownership and cloud limits retained. | First and second PC, wrong key, expired session, network interruption and recovery after restart. |
| Settings | Persistent Save/Discard footer, section shortcuts, Ctrl+S, non-modal success feedback, draft indicator, overlay/sound defaults and explicit device-local Windows section. Account sync refuses unsaved drafts; tray exit warns about unsaved Settings edits. Connection changes are guarded against unrelated drafts, and saving preferences preserves an unverified Xbox key. | Edits during all connection operations and system shutdown/update scenarios. |
| Companion/library/history | Library search and three sort orders, better empty states, imported-history labels and disabled delivery recovery actions on historical items. Per-game drafts survive background refresh and cannot be silently replaced by another selection. Existing gallery filters, pins, posters and recaps retained. | Large real library, long names, missing artwork and changing filters with unsaved per-game preferences. |
| Overlay customisation | Direct route from Presentation to detailed controls; value labels for size/duration; existing keyboard-operable placement presets, real preview, motion controls and fullscreen guidance retained. Added a safe unsaved reset of master overlay/sound defaults. | Actual multi-monitor placement, exclusive fullscreen, DPI changes and consecutive live unlocks. |
| Sound and quiet | One shared Companion save footer, explicit master-switch explanation, current persisted mute state consulted for previews, Stop preview, visible volume and silent-preview feedback. Existing timed mute/hide/hold/resume controls retained. | Audio output-device changes, quiet-mode expiry and restart. |
| Discord delivery | Everyday primary action no longer sends a test. Preview/local replay labels remain explicit. Historical import cannot offer retry/confirmation. Advanced shared-folder coordination explains its interaction with cloud claims. Delivery safety and pending/uncertain states are otherwise retained. | Dedicated test channel: failed, rejected, ambiguous and cross-PC delivery. |
| Tray and daily use | Existing close-to-tray explanation, reopen, test, mute/resume and explicit Exit retained. New exit draft reminder; hidden Settings drafts pause sync. | Reopen/focus and Windows startup with saved credentials. |
| Visual consistency | Account and Companion use shared Window styling, default text uses the semantic foreground brush, save controls are consistent, Account gets a labelled navigation icon, decorative spaced header copy simplified. Technical coordination details collapsed. | Complete colour/contrast audit of every interaction state, not just the rendered resting screens. |
| Accessibility/layout | Main minimum width reduced to 900 with responsive Home; navigation scrolls in short windows. F1/Ctrl+S, named fields/sliders, live status regions and value labels added. Existing focus borders and motion opt-outs retained. | Keyboard-only and screen-reader walkthrough; Windows high contrast and 125–200% scaling on actual displays. |
| Feedback/errors | Specific account recovery guidance and cancellation, guarded duplicate account actions, persistent draft/save feedback, searchable activity with warnings-only filter, actionable no-results messages. Existing connection/update progress retained. | Slow/offline operation, repeated clicks across every non-account action, and notification frequency over extended use. |
| Install/updates/help | Help explains navigation, shortcuts, pairing and posting. Getting Started and account guide updated; uninstall guide explains cloud-data/key consequences. Existing signed updater and installer contract retained. | Upgrade from v0.11.0, cancellation/retry, uninstall and reinstall with retained local data. |
| Final acceptance | Release build, core/app contract tests and repository checks; native render export covers six main pages, Account, nine Companion tabs and compact Home. Export also asserts draft detection, navigation preservation and discard. | The live device cases above are acceptance work, not asserted as complete. |

## Product decisions

- Keep sign-in optional and Supabase Free only.
- Keep startup, display placement and Steam account device-specific.
- Keep explicit Save for Settings and Companion preferences. Connection checks, disconnects, pins and quiet actions remain individually labelled actions.
- Keep the existing one-account-per-Windows-profile boundary. Switching accounts or deleting the cloud account is not a cosmetic action: the current UI explains that these are unavailable. No account or production data was deleted during this review.
- Keep recovery-key backup necessary for another PC. The service cannot recover a lost key.
- Retain installer/signing/delivery behaviour; do not combine a UI change with a new update policy or historical-posting rule.

## Reproduce the review

Build Release and run `--export-redline-preview <output-directory>/home.png` with the desktop executable. It creates isolated temporary storage, starts no monitoring or tray service, sends no Discord messages, renders the main/Companion/account pages and deletes the isolated data afterward. CI retains the preview images.

The dashboard export covers empty/local states. The animated export also runs the isolated usability acceptance cases below. Neither replaces physical-device or real-account acceptance.

## Additional isolated acceptance

The `--export-unlock-sequence <directory>` runner now exercises real WPF controls against temporary storage with 300 journal entries and 100 games. Its `usability-verification.txt` records:

- Failed Settings writes retain edits, leave saved settings unchanged and restore usable controls.
- Failed Companion writes retain edits; retry persists the draft.
- Imported history stays out of local session recaps and is labelled correctly in trophies.
- Historical delivery recovery buttons are disabled, and the underlying handlers are inert.
- Per-game drafts survive filtering. Discard applies the pending filter without leaving stale draft state.
- Library search, empty-result clearing and name sorting work at the supported 100-game limit.
- Long titles wrap and missing artwork uses the branded fallback in populated native renders.

The review also added Companion dropdown focus outlines, named lists and initial work-area sizing. Companion saves now mark only the submitted control snapshot as saved, preserving edits made while a write is pending. Library selection updates are no longer skipped by an unrelated busy action.

Still unverified: real-account and two-PC flows, physical monitor/audio changes, Windows high contrast, assistive-technology walkthroughs, and installer upgrade/uninstall on a separate Windows installation. No production cloud data, credentials or Discord posts are used by these isolated checks.
