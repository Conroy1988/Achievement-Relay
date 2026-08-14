# Installation

## Supported systems

- Windows 10 version 2004 (build 19041) or newer
- Windows 11
- x64 and Arm64 processors
- Internet access to `discord.com`

Release packages are self-contained, so users do not need to install .NET separately.

## Recommended install

1. Download `AchievementRelay_Setup.exe` from the latest official GitHub Release.
2. Double-click it and select **Install**.
3. If Microsoft Defender SmartScreen appears for this early alpha, verify that the file came from the official Achievement Relay release, select **More info**, and then **Run anyway**.
4. If setup asks for administrator approval, approve the one-time development-certificate import.
5. Follow the app's four-step Guided setup when Achievement Relay opens.

The setup executable contains both supported packages, chooses the native x64 or Arm64 build, installs the MSIX for the signed-in user, and launches its Start-menu identity. The MSIX identity is required for Windows notification-listener permission; the setup executable does not replace or bypass that security model.

If the release includes a self-signed development certificate, Windows requires that certificate in **Local Computer → Trusted People**. Setup requests administrator approval only for that import and then returns to a per-user installation. A production-signed release does not need this trust step.

## Manual fallback bundle

If the setup executable is blocked by local policy, download and extract `AchievementRelay_<version>_installer.zip`, then run `Install.ps1` with PowerShell from inside the extracted folder. If right-click **Run with PowerShell** closes immediately, open PowerShell in that folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
```

Review `Install.ps1` before running it. The execution-policy flag applies only to that PowerShell process. The script performs the same package selection, certificate import, MSIX installation, and launch as the setup executable.

## Alpha signing notice

When no production certificate is configured, the release workflow creates a short-lived development signing certificate and includes only its public `.cer` file. `Install.ps1` imports that public certificate into `Cert:\LocalMachine\TrustedPeople` with administrator approval so Windows can verify and install the package.

Development certificates are appropriate for testers, not a final distribution channel. Microsoft Defender SmartScreen may warn about the setup executable until the project builds publisher reputation or uses trusted production signing. A later build signed by a different certificate may require uninstalling the earlier alpha first. Stable releases should use a persistent trusted code-signing certificate or Microsoft Store identity.

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

You may remove an obsolete alpha certificate from **Manage computer certificates → Trusted People → Certificates** after uninstalling all packages that use it.
