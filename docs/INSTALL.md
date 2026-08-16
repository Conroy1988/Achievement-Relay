# Installation

## Supported systems

- Windows 10 version 2004 (build 19041) or newer
- Windows 11
- x64 and Arm64 processors; Steam monitoring on Arm64 requires Windows 11 x64 emulation (Xbox remains available on Windows 10 Arm64)
- HTTPS access to Discord; `api.xbl.io` is needed for Xbox and `api.steampowered.com` is used for optional Steam rarity
- the Windows Steam desktop client when Steam monitoring is enabled

Release packages are self-contained; .NET does not need to be installed separately.

## Recommended `.exe` installer

1. Download `AchievementRelay_Setup.exe` from the latest official GitHub Release.
2. Double-click it.
3. The original CRNY track **Relay Online** starts locally at 10% volume. Use **Pause music**/**Play music** at the lower-left at any time, or select **CRNY on SoundCloud** to open the [direct track page](https://soundcloud.com/daniel-conroy-224318319/crny-relay-online).
4. Choose **Connect Discord now; add OpenXBL optionally** or **Skip — I will do this later**.
5. If connecting now, paste the required Discord webhook. Paste an OpenXBL key only if using Xbox; leave it blank for Steam-only setup.
6. Toggle **Create a desktop shortcut** and select **Install**.
7. If SmartScreen appears for this beta, verify the download came from the official release, choose **More info**, then **Run anyway**.
8. If prompted, approve the one-time development-certificate trust operation.

Setup contains x64 and Arm64 MSIX packages, selects the native main app, installs for the signed-in user, creates/removes the optional desktop shortcut, and launches Achievement Relay. The soundtrack is extracted only to Setup's temporary directory, loops through a private Windows Media Player instance at 10% volume with an independently limited MCI fallback, does not alter Windows master volume, stops on every exit path, and is not installed with the app. Both packages contain a small isolated x64 Steamworks helper; Windows 11 on Arm runs that helper under x64 emulation. On Windows 10 Arm64 the app reports Steam as unavailable instead of entering a retry loop, while Xbox remains supported.

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

Add `-CreateDesktopShortcut` if wanted. The manual path does not collect credentials; complete Setup when the app opens.

The execution-policy flag applies only to that PowerShell process. The script selects the package, imports the included development certificate if necessary, installs the MSIX, optionally creates the shortcut, and launches the app.

## Direct MSIX installation

1. Import `AchievementRelay.Development.cer` into **Local Computer → Trusted People** if included.
2. Double-click the MSIX matching the processor.
3. Select **Install**.
4. Launch Achievement Relay from Start and complete the four-step Setup flow.

## Upgrade

Run the newer `.exe` installer. The package identity preserves `%LOCALAPPDATA%\AchievementRelay`.

From version 0.3 onward, the app checks the official GitHub Releases feed automatically. Home and **Help & support** show a newer stable release and provide **Update now**. The app first verifies the manifest's detached RSA signature against the publisher certificate pinned into the installed build. Before Setup can open, it also requires the release tag and signed product/package versions to agree, bounds the download, verifies its SHA-256 and exact product/file version resources, asks Windows to validate its Authenticode signature, and matches the signer against the same publisher pin. A normal update is optional. Monitoring pauses only when an authenticated release manifest explicitly raises the minimum supported product version above the installed version.

The verified executable opens this same installer in update mode. Update mode plays **Relay Online** through the same private 10% volume paths and exposes the same Pause/Play and direct SoundCloud controls, but skips credentials and player options. It preserves `%LOCALAPPDATA%\AchievementRelay`, the current startup preference, and whether the desktop shortcut already exists. If Setup is cancelled before deployment, the current app remains running. If package deployment fails after closing it, Setup attempts to relaunch the still-installed version.

Upgrading from 0.1.x retains the encrypted Discord webhook and preferences, then reopens Setup so a current source can be selected. Upgrading from 0.2 retains Xbox/Discord settings and cursors. Steam is enabled locally and gives every Steam account/game pair a silent first baseline, so the upgrade cannot post old Steam history.

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
