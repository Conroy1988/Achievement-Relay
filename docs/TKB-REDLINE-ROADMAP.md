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

## Stage 2 — collection and activity (this branch)

- Shared TKB two-bar identity and more readable collection/gallery text.
- Steam/Xbox, in-progress, completed and unknown-total library filters.
- Progress cards derived only from valid snapshot totals; unknown is never 0% completion.
- Honest “Recently observed” sorting, explicitly distinct from last-played activity.
- Search within imported history and the selected session timeline; timeline filters never alter a shared recap.
- Native empty/populated/compact fixtures and projection regression checks.
- No provider, delivery, account sync, persistent setting or release-version changes.

## Remaining approved stages

1. Finish native validation of collection/activity changes before landing.
2. Visual card/overlay studio, desktop and Discord-size previews, positioning and scaling controls.
3. Event-driven motion and completion celebration with reduced-motion support.
4. Optional preview-first session recaps and verified completion shelf.
5. Tray, startup, display-scaling and sync-state polish; clearer delivery explanations without unsafe retry controls.

Do not represent these remaining stages as shipped with the visual foundation. The foundation does not change persistent settings or enable new Discord posts.
