# Security policy

## Supported versions

Achievement Relay is pre-1.0. Security fixes are applied to the latest release and default branch. Version 0.1.x is functionally obsolete and is not supported for achievement capture.

## Report a vulnerability

Use GitHub's **Report a vulnerability** private security-advisory form for this repository. Do not open a public issue for a vulnerability that could expose API keys, webhook tokens, account identifiers, arbitrary code execution, package-signing material, or another user's data.

Include the affected version, Windows version, reproducible steps, impact, and suggested mitigation. Remove or replace all secrets before attaching logs or screenshots.

## Secret safety

An OpenXBL API key and Discord webhook URL are bearer credentials. Never paste either into an issue, chat, screenshot, commit, test fixture, installer log, or support summary. If exposed:

- revoke/create a new OpenXBL key from the OpenXBL profile; and
- delete or rotate the Discord webhook in Discord.

Achievement Relay:

- accepts only a bounded, non-whitespace OpenXBL key value;
- accepts only HTTPS webhook URLs on approved Discord-owned hosts;
- encrypts both saved values with current-user Windows DPAPI and separate entropy values;
- does not place installer-entered credentials on a command line;
- durably stores fresh DPAPI ciphertext before deleting the one-time installer handoff;
- redacts Discord-webhook-shaped log content; and
- disables Discord mention parsing.

DPAPI protects secrets at rest from other ordinary Windows users; it does not protect against malware or an administrator operating inside the same signed-in user context.

## Provider and network boundary

The API key is sent only to OpenXBL's `https://api.xbl.io/` service in the `X-Authorization` header. Route negotiation changes only the path on that fixed HTTPS origin. Discord payloads are sent only to a URL accepted by `WebhookUrlValidator`. HTTP responses are size-bounded and requests use timeouts. No provider response is treated as executable content.

Steam monitoring needs no credential. A narrow out-of-process helper reads local Steamworks state for the detected App ID and emits versioned JSON snapshots over redirected standard I/O. It is bundled with the reviewed MIT-licensed Facepunch.Steamworks 2.5.2 package; the repository check pins its SHA-256. The helper has no settings, webhook, or Steam mutation code. Public rarity requests are fixed to `https://api.steampowered.com/`; optional Collector Card hero art uses an allowlisted Steam CDN URL derived from the numeric App ID. Neither request carries a personal key.

Collector Card inputs remain untrusted presentation data. Provider percentages must be finite and within 0–100; text is normalized and bounded before layout. Optional remote artwork is accepted only through a bounded HTTPS fetch with redirect, response-size, content and decoded-dimension controls. The fetch carries no OpenXBL key or Discord webhook. Rendering occurs only after an event has independently passed the live-unlock rules, and a card failure cannot authorize an event or disable text-only delivery.

The local Signal Strip consumes the already-bounded eligible-event presentation data and never captures screen or input content. Its non-activating, click-through window is presentation-only: overlay failure, closure or opt-out cannot mark a provider event processed, acknowledge Discord delivery, or weaken retry and deduplication state.

The finished card is sent as one bounded Discord attachment. Its filename is constant rather than provider-controlled, the JSON still disables mention parsing, and the achievement's important facts remain ordinary embed text. Card or artwork bytes are not written to the application log.

OpenXBL, Valve/Steam, and Discord are independent third parties. A compromise or behavioral change at a provider is outside Achievement Relay's security boundary; users can revoke the Xbox key, disable Steam monitoring, or remove the webhook at any time.

## Release integrity

Official self-updating releases use the protected private key for the persistent, project-owned **Achievement Relay Open Source** certificate and RFC 3161 timestamping. The certificate is self-signed, RSA-3072, code-signing-only, and not a certificate authority. Its public DER certificate and metadata are reviewable in `release/`; its private PFX exists only in protected recovery storage and masked GitHub Actions secrets. Development-signed pull-request packages use different temporary identities whose private keys are not included in artifacts and cannot form a trusted update chain across builds.

The app accepts update metadata only from the official repository's latest stable GitHub Release. The exact manifest bytes have a detached RSA/SHA-256 signature made by the release certificate; the embedded certificate must have the code-signing EKU and its SHA-256 fingerprint must match a pin in the running app. That signature is rechecked whenever cached policy is used. The release tag, manifest product/package versions, exact asset names, GitHub URLs, installer size, SHA-256, and embedded Windows product/file versions must then agree. The signed Windows package version must also be numerically capable of upgrading the installed package. Windows validates the installer's Authenticode signature and the signer must match the same pin. The installer is launched only after the file checks are repeated immediately before execution. A reviewed `minimumSupportedVersion` in `release/update-policy.json` is the sole mechanism that can make an update required and pause monitoring; network failure or unauthenticated metadata never does so.

The first official installation includes the public project certificate and, with explicit administrator approval, adds only that leaf to **Local Computer → Trusted People**. This is necessary because the certificate does not chain to a commercial Windows trust root. It may also produce a SmartScreen reputation warning. After that one-time trust decision, Windows Authenticode validation and the app's independent SHA-256 certificate pin both protect automatic updates.

Keep the signing key out of the repository and use the same protected publisher identity for later releases. To rotate a certificate, first ship a transition release signed by the currently trusted certificate that pins both the old and replacement fingerprints; only then sign a later release with the replacement. Never replace a release installer or manifest in place after publication—publish a higher version instead.
