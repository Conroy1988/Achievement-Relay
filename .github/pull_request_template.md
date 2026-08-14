## Summary

Describe the user-visible outcome and why this change is needed.

## Validation

- [ ] `dotnet build .\AchievementRelay.sln --configuration Release`
- [ ] Core contract checks pass
- [ ] Repository/MSIX checks pass
- [ ] Tested from an installed MSIX when notification or startup behaviour changed

## Privacy and security

- [ ] No Discord webhook URL, token, private notification, gamertag, certificate, or signing key is included
- [ ] Source filtering still occurs before notification text is read
- [ ] Documentation reflects any permission, storage, logging, or network change
