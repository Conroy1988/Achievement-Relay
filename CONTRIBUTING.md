# Contributing

Thank you for helping make Xbox achievement notifications on Discord more reliable.

## Before opening an issue

- Search existing issues.
- Work through [Troubleshooting](docs/TROUBLESHOOTING.md).
- Use **Diagnostics → Copy support summary**.
- Never include a Discord webhook URL or token.
- Include Windows version/language, Xbox app or Game Bar version, game name, and whether the original Xbox notification appeared.

For parsing bugs, share only a manually redacted transcription or crop of the Xbox notification. Remove gamertags, messages, friend names, and unrelated notifications.

## Development

1. Fork and create a focused branch.
2. Keep platform-neutral behavior in `AchievementRelay.Core` where practical.
3. Add or update a contract check for classifier, parser, URL, payload, or fingerprint changes.
4. Run:

   ```powershell
   dotnet build .\AchievementRelay.sln --configuration Release
   dotnet run --project .\tests\AchievementRelay.Core.Tests --configuration Release
   .\scripts\Test-Repository.ps1
   ```

5. Test notification capture from an installed MSIX; package identity is required.
6. Explain privacy implications in the pull request whenever notification access, logging, local storage, networking, or webhook handling changes.

## Parser contributions

Prefer conservative, source-gated rules. An achievement missed is recoverable with a parser update; unrelated private notification text sent to Discord is not. Add localized unlock phrases only with a representative redacted fixture and the expected parsed fields.

## Pull requests

Keep each pull request small, document user-visible changes, preserve the first-run path, and update relevant docs. All CI checks must pass. By contributing, you agree that your contribution is licensed under the MIT License.
