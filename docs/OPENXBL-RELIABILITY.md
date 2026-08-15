# OpenXBL reliability research and operating contract

Last reviewed: 2026-08-15

This document records the provider research, live Windows findings, detection invariants, and acceptance gates for Achievement Relay. It deliberately contains no API key, webhook, XUID, gamertag, or raw private account response.

## What the upstream contracts actually guarantee

OpenXBL's published OpenAPI file documents the current `X-Authorization` header and the account, title-history, modern achievement, and dedicated Xbox 360 achievement routes. It does not publish response schemas for the achievement routes or a numeric request allowance. Achievement Relay therefore treats the documented paths as route discovery, not as a complete JSON contract, and respects HTTP `Retry-After` rather than hard-coding an undocumented plan limit.

Primary references:

- [OpenXBL OpenAPI specification](https://github.com/OpenXBL/Docs/blob/main/openapi.yaml)
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

The live historical-flood regression established a stricter rule: uncertainty must cost a missed notification, never a backlog. The affected build was stopped immediately; its credentials and durable event ledger remain valid. Schema 4 repairs the state in place, preserves already known identities, and silently records any unverified historical identities before normal monitoring continues.

## Detection invariants

Achievement Relay uses these rules:

1. **Identity, not time, defines an event.** The deterministic ID is a SHA-256 hash of a version marker, account XUID, service configuration, title ID, and achievement ID.
2. **The durable per-title snapshot stores all currently unlocked identities.** A new unlock is the set difference between the current complete detail set and the previous set.
3. **A missing or sentinel time never discards an achieved entry.** The app uses the observation time for the Discord embed and labels it as estimated.
4. **Summary/detail disagreement never advances state.** The parsed unlocked-identity count must exactly match title history; whether detail is behind or ahead, the poll retries until both views converge.
5. **A failed Discord delivery never advances the title snapshot.** Already processed deterministic IDs remain in the ledger, so a retry does not repost earlier successes from the same poll.
6. **Provider pages cannot erase history.** Titles absent from a later title-history response remain in local state; if they reappear, they are compared with their retained snapshot rather than treated as a new game.
7. **Old installs and newly revealed titles fail closed.** Counts and Gamerscore never authorize a Discord post when a title has no verified identity set. Only a usable provider timestamp strictly after the app's monitoring baseline can prove such an event is new. Old, missing-time, sentinel-time, and otherwise unproven entries are stored silently as the title's complete identity baseline; later set differences are exact.
8. **First connection is still a no-spam baseline.** Existing achievements are never posted merely because the app was installed.
9. **Identity baselines are hydrated gradually.** A summary count, including zero, is never treated as a verified ID set. Each otherwise-successful poll hydrates one unchanged, most-recent unverified title without posting, so fresh and upgraded installs converge to exact timestamp-independent detection without a burst of provider calls.
10. **Provider regressions cannot erase durable history.** Saved counts, Gamerscore, and identities do not shrink when a partial or changed provider representation reports less data. If a route suddenly represents more identities than the summary increase can explain, the app baselines the representation change instead of flooding historical achievements.

## State and delivery transaction

For each one-minute poll:

1. fetch the current title-progress index;
2. retain local snapshots for titles omitted from the response;
3. fetch details for titles whose count or Gamerscore increased;
4. keep probing compatible modern/Xbox 360 routes until a parsed result reaches the reported count;
5. compute stable-ID differences;
6. send each new event to Discord with `wait=true` so an HTTP success confirms creation rather than only queue acceptance;
7. persist each processed event ID immediately;
8. save the new per-title identity sets and successful-poll time only after all required deliveries finish.

After required changed-title work succeeds, the same poll may fetch one recent unchanged title that still has only a count baseline. That low-priority hydration never creates a Discord post and its failure does not prevent required sync state from advancing; any provider `Retry-After` still controls the next background interval.

Discord webhooks do not provide an idempotency key. `wait=true`, deterministic local IDs, and write ordering reduce duplicate risk substantially, but no client can prove exactly-once delivery if Windows terminates it in the narrow instant after Discord creates a message and before the local ledger write completes. The supported delivery model is therefore durable at-least-once retry with practical deduplication, not a false exactly-once claim.

## Failure and retry policy

| Condition | State advancement | User-visible behavior |
|---|---|---|
| Invalid/rejected key | No | Guided setup remains actionable; stored secret stays encrypted |
| Network timeout/5xx | No | Safe error; automatic retry |
| HTTP 429 | No | Honor `Retry-After`; capped background wait |
| Route 400/404 during negotiation | No | Try the next documented/compatible route |
| Readable detail count differs from title index | No | Retry without moving the sync position |
| Missing/sentinel unlock time with known ID baseline | Yes after delivery | Post with detected time labelled estimated |
| Unverified title with a valid post-baseline timestamp | Yes after delivery | Post only the proven post-baseline event; baseline the full identity set |
| Unverified title with old, missing, sentinel, or otherwise unproven times | Yes, silent baseline | Do not infer from counts/Gamerscore; post nothing historical; future changes become exact |
| Discord 401/403/404 | No | Actionable webhook error |
| Discord 429/5xx/timeout | No | Bounded retry, then next poll |

## Security boundary

- OpenXBL keys and Discord webhook URLs are current-user DPAPI ciphertext at rest.
- Secrets are never command-line arguments, activity-log values, support-summary values, or research-document content.
- Raw provider responses are not persisted because they can contain account identifiers and private profile data.
- Transport exception text is not surfaced for Discord requests because platform messages can embed the credential-bearing webhook URI.
- Discord payloads disable mention parsing, validate the webhook host/path, canonicalize the legacy Discord host, refuse redirects that could forward a token, truncate every user/provider string to Discord limits, and use a declared product user agent.

## Automated and live acceptance matrix

Automated checks must cover:

- current, nested, wrapped, and string-encoded account/title envelopes;
- modern achieved/locked/revoked entries;
- Xbox 360 boolean unlocks with real, sentinel, and missing times;
- deterministic account-specific identities;
- known-ID set differences independent of time;
- silent newly discovered historical-title baselines, post-baseline timestamp proof, and a prohibition on count/Gamerscore inference;
- incomplete summary/detail responses;
- mention suppression and estimated-time disclosure;
- state schema 4, omitted-title retention, route ordering/cache, rate-limit handling, installer versioning, and running-app shutdown.

The Windows release gate is not complete until all of these pass on the generated installer:

1. upgrade over the immediately previous test build while the tray app is running;
2. installer-entered OpenXBL and Discord values reappear masked and can be revealed explicitly;
3. Save and connect resolves the intended account and complete title history;
4. Finish setup succeeds without reopening the setup-required dialog;
5. Sync now reaches an up-to-date/monitoring state without a repeating warning;
6. two consecutive syncs send no achievement dated before the monitoring baseline, including a title first revealed on a later provider page;
7. a newly earned modern achievement posts exactly once;
8. a newly earned Xbox 360/backward-compatible achievement with no usable provider time posts exactly once with the detected-time footer; and
9. restart/retry does not repost either event.

External Xbox/OpenXBL/Discord availability cannot be made infallible by a desktop client. The release criterion is that every supported upstream response or failure is handled deterministically, securely, without a stuck cursor, and without an avoidable duplicate or historical flood.
