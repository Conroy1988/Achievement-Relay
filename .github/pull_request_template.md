## Summary

Describe the user-visible outcome and why this change is needed.

## Validation

- [ ] `dotnet build .\AchievementRelay.sln --configuration Release`
- [ ] Core contract checks pass
- [ ] Repository/MSIX checks pass
- [ ] Tested from the installer/MSIX when account sync, startup, or setup behaviour changed

## Privacy and security

- [ ] No OpenXBL key, Discord webhook URL/token, XUID, gamertag, certificate, or signing key is included
- [ ] Secret handoffs, logs, diagnostics, and error messages remain redacted
- [ ] Documentation reflects any permission, storage, logging, or network change
