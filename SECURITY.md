# Security policy

## Supported versions

Achievement Relay is currently pre-1.0. Security fixes are applied to the latest release and the default branch.

## Report a vulnerability

Use GitHub's **Report a vulnerability** private security-advisory form for this repository. Do not open a public issue for a vulnerability that could expose notification contents, webhook tokens, arbitrary code execution, package-signing material, or another user's data.

Include the affected version, Windows version, reproducible steps, impact, and any suggested mitigation. Remove or replace all secrets before attaching logs or screenshots.

## Webhook safety

A Discord webhook URL contains a bearer token. Never paste it into an issue, chat, log excerpt, screenshot, commit, or test fixture. If one is exposed, delete or rotate the webhook in Discord immediately and replace it in Achievement Relay.

The application accepts only HTTPS webhook URLs on Discord-owned hosts, encrypts the stored URL with current-user DPAPI, redacts webhook-looking log content, and disables Discord mention parsing.

## Release integrity

Production releases should use a protected, persistent code-signing certificate. Development-signed alpha packages include their public certificate only; the generated private key is not included in release assets. Verify downloaded package signatures before installation when security is important.
