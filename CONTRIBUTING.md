# Contributing

Thank you for helping make Xbox achievements on Discord more reliable.

## Before opening an issue

- Search existing issues and work through [Troubleshooting](docs/TROUBLESHOOTING.md).
- Use **Diagnostics → Copy support summary**.
- Never include an OpenXBL API key, Discord webhook URL/token, XUID, gamertag, or unredacted provider response.
- Include the app version, Windows version, game/title name, approximate unlock time, whether **Sync Xbox now** succeeds, and the exact redacted error.

Provider payload examples must be synthetic or thoroughly redacted. Replace account/title identifiers, image locators, gamertags, and credentials while preserving the JSON structure needed to reproduce the parser problem.

## Development

1. Fork the repository and create a focused branch.
2. Keep provider-response parsing and Discord payload behavior in `AchievementRelay.Core` where practical.
3. Add or update a contract check for OpenXBL shapes, URL validation, identities, or payload changes.
4. Run:

   ```powershell
   dotnet build .\AchievementRelay.sln --configuration Release
   dotnet run --project .\tests\AchievementRelay.Core.Tests --configuration Release
   .\scripts\Test-Repository.ps1
   ```

5. Test the installed build on Windows. Verify first baseline, one real post, manual sync, Discord failure/retry, tray behavior, startup, upgrade, and uninstall.
6. If installer behavior changes, build `AchievementRelay_Setup.exe` and test both **Connect now** and **Skip — configure later**, plus the desktop-shortcut toggle.
7. Explain privacy implications whenever authentication material, account data, local storage, logging, networking, or installer handoff behavior changes.

## Parser contributions

Prefer strict provider-schema handling:

- accept only achievements explicitly marked achieved and not revoked;
- require a valid unlock timestamp and stable achievement identifier;
- tolerate harmless JSON property casing and documented alternate fields;
- ignore incomplete entries instead of inventing values; and
- derive deterministic event IDs that include the account and achievement identity.

## Pull requests

Keep each pull request focused, document user-visible behavior, preserve the no-history-flood guarantee, and update relevant docs. All CI checks must pass. Contributions are licensed under the MIT License.
