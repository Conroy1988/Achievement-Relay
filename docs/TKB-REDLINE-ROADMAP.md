# TKB Redline upgrade

## Safety boundary

Deliver this as reviewable stages. Preserve provider detection, deduplication, uncertain-delivery handling, account sync and existing settings. Never use dashboard projections as delivery evidence. No automatic recap posting by default. Native Windows checks and screenshots are required before landing each UI stage; installed live-game testing remains a separate check.

## Stage 1 — visual foundation (merged in #25)

- Shared equal-bar vector branding matching the existing Collector Card geometry.
- Larger artwork-first current/last-game hero and verified progress bar.
- Read-only local-date unlock count, known rarity and delivery-attention summary.
- Restore recent delivered achievements from the existing local journal after restart.
- Hide the sample achievement panel until there is real delivered activity.
- Clear distinction between a local overlay preview and a Discord test post.
- Keep accessible control contrast, settings draft protections and compact layouts.

## Stage 2 — collection and activity (merged in #26)

- Shared TKB two-bar identity and more readable collection/gallery text.
- Steam/Xbox, in-progress, completed and unknown-total library filters.
- Progress cards derived only from valid snapshot totals; unknown is never 0% completion.
- Honest “Recently observed” sorting, explicitly distinct from last-played activity.
- Search within imported history and the selected session timeline; timeline filters never alter a shared recap.
- Native empty/populated/compact fixtures and projection regression checks.
- No provider, delivery, account sync, persistent setting or release-version changes.

## Stage 3 — presentation studio (merged in #27)

- Separate preview-only window, opened from Gallery or Presentation without replacing game artwork.
- Real Collector Card renderer at desktop/small/full-artwork logical widths, constrained to the window.
- Actual compact embed text, explicitly labelled as an approximate layout rather than a Discord screenshot.
- Selected achievement or clearly labelled Steam/Xbox synthetic unlock, rare and completion examples.
- Real signal-strip static reference and explicit local animated preview using the existing overlay service.
- Draft presentation snapshot, current master sound settings, existing quiet/reduced-motion protections; no settings or delivery mutations.
- Native Steam/Xbox/compact fixtures assert that static previews do not play alerts or change journal, settings or delivery state.

## Stage 4 — preview-first recaps and completion shelf (this branch)

- Recap counts exclude imported history and synthetic completion events, deduplicate recorded unlocks and retain Steam/Xbox identity.
- Bounded, mention-suppressed message built once and previewed before an explicit send. Cancel is the default action; no automatic recap posting or retry.
- Read-only 100% completion shelf in Trophies, using valid snapshot totals only; inspecting a game opens its Library details.
- Existing beam reveal, rare/completion particles, countdown and chimes are preserved. Native CI already exercises the real button → queue → overlay path and verifies static/reduced-motion suppression; no duplicate animation system was added.
- No achievement delivery, baseline, sync or settings schema changes.

## Remaining approved stages

1. Validate and land recaps and the completion shelf.
2. Tray, startup, display-scaling and sync-state polish; clearer delivery explanations without unsafe retry controls.
3. Final release validation and versioning. Installed live-game testing remains separate from CI.

Do not represent these remaining stages as shipped with the visual foundation. The foundation does not change persistent settings or enable new Discord posts.
