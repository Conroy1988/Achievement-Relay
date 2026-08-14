# Installation

## Supported systems

- Windows 10 version 2004 (build 19041) or newer
- Windows 11
- x64 and Arm64 processors
- Internet access to `discord.com`

Release packages are self-contained, so users do not need to install .NET separately.

## Recommended install

1. Download and extract `AchievementRelay_<version>_installer.zip`.
2. Run `Install.ps1` with PowerShell from inside the extracted folder.
3. Follow the app's four-step Guided setup.

The script chooses the native x64 or Arm64 package, installs the MSIX for the current user, and launches its Start-menu identity. If the release includes a self-signed development certificate, Windows requires that certificate in **Local Computer → Trusted People**; the script requests administrator approval only for that import. A production-signed release does not need this trust step.

If right-click **Run with PowerShell** closes immediately, open PowerShell in the extracted folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

Review `Install.ps1` before running it. The execution-policy flag applies only to that PowerShell process.

## Alpha signing notice

When no production certificate is configured, the release workflow creates a short-lived development signing certificate and includes only its public `.cer` file. `Install.ps1` imports that public certificate into `Cert:\LocalMachine\TrustedPeople` with administrator approval so Windows can verify and install the package.

Development certificates are appropriate for testers, not a final distribution channel. A later build signed by a different certificate may require uninstalling the earlier alpha first. Stable releases should use a persistent trusted code-signing certificate or Microsoft Store identity.

## Manual install

To install without the helper script:

1. Import `AchievementRelay.Development.cer` into **Local Computer → Trusted People** if it is included. This requires administrator rights.
2. Double-click the MSIX matching your processor.
3. Select **Install**.
4. Launch Achievement Relay from Start.

## Upgrade

Install a newer package with the same identity and signing certificate. Windows preserves `%LOCALAPPDATA%\AchievementRelay`, including settings and the encrypted webhook.

## Uninstall

Use **Settings → Apps → Installed apps → Achievement Relay → Uninstall**, or run:

```powershell
.\Uninstall.ps1
```

Local settings are intentionally preserved. To remove settings, logs, the processed-event ledger, and the encrypted webhook too:

```powershell
.\Uninstall.ps1 -RemoveLocalData
```

You may remove an obsolete alpha certificate from **Manage user certificates → Trusted People → Certificates** after uninstalling all packages that use it.
