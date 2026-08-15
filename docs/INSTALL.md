# Installation

## Supported systems

- Windows 10 version 2004 (build 19041) or newer
- Windows 11
- x64 and Arm64 processors
- HTTPS access to `xbl.io` and Discord

Release packages are self-contained; .NET does not need to be installed separately.

## Recommended `.exe` installer

1. Download `AchievementRelay_Setup.exe` from the latest official GitHub Release.
2. Double-click it.
3. Choose **Connect OpenXBL and Discord now** or **Skip — I will do this later**.
4. If connecting now, paste the API key and Discord webhook into the masked fields.
5. Toggle **Create a desktop shortcut** and select **Install**.
6. If SmartScreen appears for this beta, verify the download came from the official release, choose **More info**, then **Run anyway**.
7. If prompted, approve the one-time development-certificate trust operation.

Setup contains x64 and Arm64 MSIX packages, selects the native architecture, installs for the signed-in user, creates/removes the optional desktop shortcut, and launches Achievement Relay.

The optional credentials are never added to PowerShell arguments. Setup passes them to a short-lived protection process through inherited environment variables, writes only current-user DPAPI ciphertext under `%USERPROFILE%\.achievement-relay`, clears its fields/environment, and launches the app. The app durably stores fresh encrypted settings before deleting the one-time handoff and starting live checks. Choose **Skip** to create no handoff at all.

## Signing notice

When a production certificate is not configured, the release workflow creates a temporary development signing certificate and includes only its public `.cer`. `Install.ps1` imports that public certificate into **Local Computer → Trusted People** with administrator approval so Windows can validate the MSIX. Setup itself otherwise operates per user.

Development signing is suitable for beta testers, not a final distribution channel. SmartScreen can warn until the project uses a trusted certificate and builds reputation. A build signed by a different development certificate may require uninstalling the older package first.

## Manual fallback bundle

If the `.exe` installer is blocked by local policy:

1. download and extract `AchievementRelay_<version>_installer.zip`;
2. review `Install.ps1`;
3. open PowerShell in the extracted folder; and
4. run:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1
   ```

Add `-CreateDesktopShortcut` if wanted. The manual path does not collect credentials; complete Guided setup when the app opens.

The execution-policy flag applies only to that PowerShell process. The script selects the package, imports the included development certificate if necessary, installs the MSIX, optionally creates the shortcut, and launches the app.

## Direct MSIX installation

1. Import `AchievementRelay.Development.cer` into **Local Computer → Trusted People** if included.
2. Double-click the MSIX matching the processor.
3. Select **Install**.
4. Launch Achievement Relay from Start and complete Guided setup.

## Upgrade

Run the newer `.exe` installer. The package identity preserves `%LOCALAPPDATA%\AchievementRelay`.

Upgrading from 0.1.x retains the existing encrypted Discord webhook and preferences, then reopens Guided setup because 0.2 requires an OpenXBL key. The first verified account connection creates a baseline and does not post earlier unlocks.

## Uninstall

Use **Settings → Apps → Installed apps → Achievement Relay → Uninstall**, or run:

```powershell
.\Uninstall.ps1
```

`Uninstall.ps1` also removes the optional desktop shortcut. Local settings remain by default. Remove settings, encrypted secrets, sync state, event ledger, and log with:

```powershell
.\Uninstall.ps1 -RemoveLocalData
```

If uninstalling directly through Windows Settings, manually remove a desktop shortcut if Windows leaves it behind. An obsolete beta certificate may be removed from **Manage computer certificates → Trusted People → Certificates** after all packages signed by it are gone.
