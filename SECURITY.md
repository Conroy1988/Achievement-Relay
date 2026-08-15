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
- deletes the one-time DPAPI-encrypted installer handoff on first launch;
- redacts Discord-webhook-shaped log content; and
- disables Discord mention parsing.

DPAPI protects secrets at rest from other ordinary Windows users; it does not protect against malware or an administrator operating inside the same signed-in user context.

## Provider and network boundary

The API key is sent only to `https://xbl.io/api/v2/` in the `X-Authorization` header. Discord payloads are sent only to a URL accepted by `WebhookUrlValidator`. HTTP responses are size-bounded and requests use timeouts. No provider response is treated as executable content.

OpenXBL is an independent third party. A compromise or behavioral change at that provider is outside Achievement Relay's security boundary; users can revoke the API key and disconnect the account at any time.

## Release integrity

Production releases should use a protected, persistent code-signing certificate. Development-signed beta packages include only their public certificate; generated private keys are not included in release assets. Verify release origin, SHA-256 hashes, and Authenticode/MSIX signatures when security is important.
