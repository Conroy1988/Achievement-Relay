# OpenXBL reliability research and operating contract

Last reviewed: 2026-08-30

This document records the provider research, live Windows findings, detection invariants, and acceptance gates for Achievement Relay. It deliberately contains no API key, webhook, XUID, gamertag, or raw private account response.

## What the upstream contracts actually guarantee

OpenXBL's published OpenAPI file documents the current `X-Authorization` header and the account, title-history, modern achievement, and dedicated Xbox 360 achievement routes, but does not publish response schemas for those achievement operations. OpenXBL's separate pricing contract currently advertises 150 requests/hour on the free plan, states that every HTTP request counts, and says the window resets hourly. Achievement Relay therefore treats the OpenAPI paths as route discovery, parses live allowance headers, and applies a conservative local ceiling rather than assuming every key has the same plan forever.

Primary references:

- [OpenXBL OpenAPI specification](https://github.com/OpenXBL/Docs/blob/main/openapi.yaml)
- [OpenXBL pricing and hourly request contract](https://xbl.io/pricing)
- [Microsoft Achievement JSON (modern Xbox)](https://learn.microsoft.com/gaming/gdk/docs/reference/live/rest/json/json-achievementv2)
- [Microsoft GET achievements](https://learn.microsoft.com/gaming/gdk/docs/reference/live/rest/uri/achievements/get-achievements)
- [Microsoft offline achievement update behavior](https://learn.microsoft.com/en-us/gaming/gdk/docs/services/player-data/achievements/title-managed/how-to/live-how-to-update-achievements)
- [OpenXbox reference models for modern and Xbox 360 achievements](https://github.com/OpenXbox/xbox-webapi-python/blob/master/xbox/webapi/api/provider/achievements/models.py)
- [OpenXbox reference achievement-provider operations](https://github.com/OpenXbox/xbox-webapi-python/blob/master/xbox/webapi/api/provider/achievements/__init__.py)
- [Discord execute-webhook contract](https://docs.discord.com/developers/resources/webhook#execute-webhook)

The relevant response families differ:

| Family | Unlock marker | Time field | Stable fields used by Relay |
|---|---|---|---|
| Modern Xbox achievement v2 | `progressState: Achieved` | `progression.timeUnlocked` | account, service configuration, title, achievement ID |
| Xbox 360 | `unlocked: true` | `timeUnlocked` | account, title, numeric achievement ID |
| Live legacy/offline variation | `unlocked: true` | missing or `0001-01-01T00:00:00Z` | same stable IDs; time is unusable |

Microsoft also documents that offline achievement updates can be queued before reaching the service. A provider timestamp is therefore useful display metadata, but it is not a safe cursor or event identity.

Modern achievement responses can also expose `rarity.currentPercentage`, while title-history responses can expose `devices` or `platforms`. Neither family is guaranteed by OpenXBL's published response schema. Achievement Relay treats them as optional display enrichment: percentages must be finite and within 0–100, and a platform is named specifically only when every relevant token is recognized and unambiguous. Unknown or mixed evidence falls back to Unranked or Xbox rather than changing delivery eligibility.

## Live failures that established the design requirements

The Windows acceptance cycle exposed each layer independently:

1. installer secrets were encrypted correctly but initially consumed before durable app storage;
2. saved fields looked empty because masked stored values were not rendered;
3. account/profile JSON appeared in more than one envelope;
4. the API-key owner's title history worked on the current-account route while an assumed XUID form returned 404;
5. Xbox 360 title details required the dedicated legacy route;
6. a readable route could still be incomplete compared with the title-history count;
7. a real backward-compatible Black Ops unlock was present and counted, but carried no usable timestamp; and
8. installer upgrades needed both an increasing MSIX version and package-broker shutdown of a running tray process; and
9. a later OpenXBL title-history page revealed complete Gears of War 2, Dawn of War II, and GTA V histories from 2009-2013, which exposed that the schema-3 count/Gamerscore migration could misclassify an entire old title as new.

These are distinct failure classes. A successful profile check is not proof of a readable title index, a readable title index is not proof of complete per-title details, and a complete detail list is not proof that every unlock has a timestamp.

The live historical-flood regression established a stricter rule: uncertainty must cost a missed notification, never a backlog. The affected build was stopped immediately; its credentials and durable event ledger remain valid. Schema 4 introduced fail-closed identity repair; schema 5 added a durable, paced detail queue; schema 6 added restart-safe live-delivery proof and fresh epochs for device handoffs; schema 7 retains normalized title-device evidence for honest, restart-safe platform labels.

## Detection invariants

Achievement Relay uses these rules:

1. **Identity, not time, defines an event.** The deterministic ID is a SHA-256 hash of a version marker, account XUID, service configuration, title ID, and achievement ID.
2. **The durable per-title snapshot stores all currently unlocked identities.** A new unlock is the set difference between the current complete detail set and the previous set.
3. **A missing or sentinel time never discards an achieved entry.** It is retained in the stable-ID baseline. It can be delivered with an estimated observation time only when its summary change was directly observed after a successful poll in the same uninterrupted live session.
4. **Summary/detail disagreement never advances state.** The parsed unlocked-identity count must exactly match title history; whether detail is behind or ahead, the poll retries until both views converge.
5. **A failed Discord delivery never advances the title snapshot.** Already processed deterministic IDs remain in the ledger, so a retry does not repost earlier successes from the same poll.
6. **Provider pages cannot erase history.** Titles absent from a later title-history response remain in local state; if they reappear, they are compared with their retained snapshot rather than treated as a new game.
7. **Old installs and newly revealed titles fail closed.** Counts and Gamerscore never authorize a Discord post when a title has no verified identity set. Only a usable provider timestamp strictly after the current live-delivery epoch can prove such an event is new. Old, missing-time, sentinel-time, and otherwise unproven entries are stored silently as the title's complete identity baseline; later set differences are exact.
8. **Every app start and long interruption is a handoff boundary.** A fresh epoch silently reconciles progress from the inactive period, preventing a PC started later from replaying another device's posts. A backwards clock jump takes the same safe path.
9. **First connection is still a no-spam baseline.** Existing achievements are never posted merely because the app was installed.
10. **Identity baselines are hydrated gradually.** A summary count, including zero, is never treated as a verified ID set. Historical queue work and unchanged baseline hydration share one background slot every 15 minutes and never run from a manual sync. Fresh and upgraded installs therefore converge without a provider burst.
11. **Provider regressions cannot erase durable history.** Saved counts, Gamerscore, and identities do not shrink when a partial or changed provider representation reports less data. If a route suddenly represents more identities than the summary increase can explain, the app baselines the representation change instead of flooding historical achievements.
12. **Request capacity is transactional too.** Each detail operation is capped at 12 requests, the process is capped at 120 requests per rolling hour, and provider remaining/reset headers can stop it earlier. Background history preserves a larger reserve than live monitoring, and no UI action bypasses the gate.
13. **Rarity is presentation, never evidence.** A global percentage can select Bronze, Silver, Gold or Platinum only after validation. Missing or malformed values select Unranked and cannot create, suppress or identify an event by themselves.
14. **Platform labels fail generic.** Exact PC-only, console-only or legacy-route evidence can select Xbox PC, Xbox Console or Xbox 360. Mixed, unknown or absent evidence remains Xbox. Device metadata never enters the achievement identity.

## State and delivery transaction

For each one-minute poll:

1. fetch the current title-progress index;
2. retain local snapshots for titles omitted from the response;
3. merge every count/Gamerscore increase into a durable pending-title queue without advancing its processed snapshot;
4. select at most one work item, prioritising identity-verified or post-baseline-played titles;
5. when no priority work exists and the 15-minute slot is due, select one old pending title or one unchanged count-only title;
6. keep probing compatible modern/Xbox 360 routes and continuation pages, but stop the operation after 12 requests;
7. compute stable-ID differences;
8. render its bounded Collector Card and send the card plus accessible embed text to Discord with `wait=true` so an HTTP success confirms creation rather than only queue acceptance;
9. persist each processed event ID immediately; and
10. commit that title's identity snapshot and remove its queue entry only after complete detail and required deliveries succeed.

The queue is part of schema 5, schema 6 adds its optional original live-delivery epoch plus untimestamped live-evidence flag, and schema 7 adds normalized device evidence to snapshots and pending work. A restart cannot turn delayed work into an updated cursor, force the title index to reveal it again, or silently change a proven platform label. A genuinely live failed delivery keeps its proof, while work migrated without proof fails closed. Low-priority failure does not mark live monitoring as failed; the item remains queued. Priority failure remains visible because a potential new unlock is awaiting exact detail or Discord delivery.

The request gate counts attempts before transport because OpenXBL counts successful, failed, and cached HTTP requests. It permits at most 120 requests in any local rolling hour, even when allowance headers are missing. When `X-RateLimit-Remaining`/limit/reset or their standard equivalents are present, background work requires a 50-request free-plan reserve plus the full operation capacity, while essential work retains a final reserve. HTTP 429 uses the published hourly fallback only when no usable reset is supplied.

Discord webhooks do not provide an idempotency key. `wait=true`, deterministic local IDs, and write ordering reduce duplicate risk substantially, but no client can prove exactly-once delivery if Windows terminates it in the narrow instant after Discord creates a message and before the local ledger write completes. The supported delivery model is therefore durable at-least-once retry with practical deduplication, not a false exactly-once claim.

## Failure and retry policy

| Condition | State advancement | User-visible behavior |
|---|---|---|
| Invalid/rejected key | No | Setup remains actionable; stored secret stays encrypted |
| Network timeout/5xx | No | Safe error; automatic retry |
| Local/provider allowance reserve reached | No | Do not send the request; resume after the calculated reset window |
| HTTP 429 | No | Honor the full `Retry-After` or hourly fallback without repeated 15-minute probes |
| Route 400/404 during negotiation | No | Try the next documented/compatible route |
| Readable detail count differs from title index | No | Retry without moving the sync position |
| Missing/sentinel unlock time with known ID baseline and direct live evidence | Yes after delivery | Post with detected time labelled estimated |
| Missing/sentinel unlock time first seen at startup/after a gap | Yes, silent baseline | Reconcile the identity without posting |
| Unverified title with a valid post-epoch timestamp | Yes after delivery | Post only the proven live event; baseline the full identity set |
| Unverified title with old, missing, sentinel, or otherwise unproven times | Yes, silent baseline | Do not infer from counts/Gamerscore; post nothing historical; future changes become exact |
| Discord 401/403/404 | No | Actionable webhook error |
| Discord 429/5xx/timeout | No | Bounded retry, then next poll |

## Security boundary

- OpenXBL keys and Discord webhook URLs are current-user DPAPI ciphertext at rest.
- Secrets are never command-line arguments, activity-log values, support-summary values, or research-document content.
- Raw provider responses are not persisted because they can contain account identifiers and private profile data.
- Transport exception text is not surfaced for Discord requests because platform messages can embed the credential-bearing webhook URI.
- Discord payloads disable mention parsing, validate the webhook host/path, canonicalize the legacy Discord host, refuse redirects that could forward a token, truncate every user/provider string to Discord limits, and use a declared product user agent.
- Collector Card artwork is optional and untrusted: remote requests carry no OpenXBL or Discord credential, and download size, redirect behavior, content, decoded dimensions and final PNG output are bounded. Rendering failure falls back without changing the delivery cursor.

## Automated and live acceptance matrix

Automated checks must cover:

- current, nested, wrapped, and string-encoded account/title envelopes;
- modern achieved/locked/revoked entries;
- Xbox 360 boolean unlocks with real, sentinel, and missing times;
- deterministic account-specific identities;
- known-ID set differences gated by timestamped or direct same-session live evidence;
- startup/gap reconciliation, silent newly discovered historical-title baselines, post-epoch timestamp proof, and a prohibition on count/Gamerscore inference;
- incomplete summary/detail responses;
- rolling-hour, provider-remaining, essential/background reserve, and reset-window request decisions;
- live-before-history queue priority and the 15-minute background eligibility boundary;
- mention suppression and estimated-time disclosure;
- finite/range-checked rarity percentages, every tier boundary, neutral Unranked behavior, distinct tier semantics and a complete no-artwork fallback;
- PC-only, console-only, Xbox 360, mixed, unknown and missing platform evidence, including retry/restart persistence without changing deterministic IDs;
- Collector Card attachment/payload agreement and accessible text retention when images are unavailable;
- state schema 7, durable live-delivery/platform evidence, omitted-title retention, route ordering/cache, rate-limit handling, installer versioning, and running-app shutdown.

The Windows release gate is not complete until all of these pass on the generated installer:

1. upgrade over the immediately previous test build while the tray app is running;
2. installer-entered OpenXBL and Discord values reappear masked and can be revealed explicitly;
3. Save and connect resolves the intended account and complete title history;
4. Finish setup succeeds without reopening the setup-required dialog;
5. Sync now reaches an up-to-date/monitoring state without a repeating warning;
6. two consecutive syncs send no achievement dated before the current delivery epoch, including a title first revealed on a later provider page;
7. a large newly revealed history becomes durable queued work, uses at most one background slot per 15 minutes, and cannot consume the final provider reserve;
8. repeated manual sync cannot exceed the rolling-hour safety ceiling;
9. a newly earned modern achievement posts exactly once ahead of queued history;
10. a newly earned Xbox 360/backward-compatible achievement with no usable provider time posts exactly once with the detected-time footer; and
11. restart/retry does not repost either event or lose the pending queue;
12. an achievement posted on device A is silently reconciled when device B starts later; and
13. the first poll after a monitoring gap longer than ten minutes is another silent handoff boundary.

External Xbox/OpenXBL/Discord availability cannot be made infallible by a desktop client. The release criterion is that every supported upstream response or failure is handled deterministically, securely, without a stuck cursor, and without an avoidable duplicate or historical flood.
