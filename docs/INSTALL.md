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
7. If SmartScreen appears, verify the download came from this repository's official release, choose **More info**, then **Run anyway** only if you intended to install Achievement Relay.
8. Approve the one-time administrator prompt that adds the public **Achievement Relay Open Source** package certificate to **Local Computer → Trusted People**. Later releases use this same identity and do not need the certificate added again.

Setup contains x64 and Arm64 MSIX packages, selects the native main app, installs for the signed-in user, creates/removes the optional desktop shortcut, and launches Achievement Relay. The soundtrack is extracted only to Setup's temporary directory, loops through a private Windows Media Player instance at 10% volume with an independently limited MCI fallback, does not alter Windows master volume, stops on every exit path, and is not installed with the app. Both packages contain a small isolated x64 Steamworks helper; Windows 11 on Arm runs that helper under x64 emulation. On Windows 10 Arm64 the app reports Steam as unavailable instead of entering a retry loop, while Xbox remains supported.

The optional credentials are never added to PowerShell arguments. Setup passes them to a short-lived protection process through inherited environment variables, writes only current-user DPAPI ciphertext under `%USERPROFILE%\.achievement-relay`, clears its fields/environment, and launches the app. The app durably stores fresh encrypted settings before deleting the one-time handoff and starting live checks. Choose **Skip** to create no handoff at all.

## Signing notice

Official v0.4.x releases use one persistent project-owned, self-signed code-signing certificate plus RFC 3161 timestamping. Its public half is committed as [`release/AchievementRelay.Publisher.cer`](../release/AchievementRelay.Publisher.cer), and its reviewed SHA-256 fingerprint is `38b45563afe0a876ed676963a271c113883437d9db7ef5d6965c8226e975df69`. The release workflow stops before packaging if the protected PFX or password is unavailable, if the PFX does not match that public certificate, or if timestamping/signature validation fails. It never falls back to a new signing identity.

Because this is an open-source project certificate rather than a certificate issued by a Windows-trusted commercial authority, the first installer can show Microsoft Defender SmartScreen and must ask for administrator approval once to add the included public certificate to **Local Computer → Trusted People**, the store [Microsoft specifies for a self-signed MSIX leaf](https://learn.microsoft.com/windows/msix/app-installer/troubleshoot-appinstaller-issues#trusted-certificates). That prompt is the user's trust decision. It does not install a certificate authority and cannot be used to trust unrelated certificates. Both architecture packages, `AchievementRelay_Setup.exe`, and the update manifest use the same code-signing-only identity. Setup otherwise operates per user. Once the certificate is trusted, the app can authenticate and install later automatic updates signed by that identity without repeating the certificate-import prompt.

Pull-request artifacts and explicit local builds still use temporary development certificates for testing. Those bundles include only their public `.cer`, and `Install.ps1` can import it into **Local Computer → Trusted People** with administrator approval. Development packages are not the public update channel, and one independently signed test build must never authenticate another. SmartScreen reputation is separate from cryptographic verification and may continue to warn for a low-volume self-signed app.

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

The execution-policy flag applies only to that PowerShell process. The script selects the package, imports the included official publisher certificate or a temporary development certificate when needed, installs the MSIX, optionally creates the shortcut, and launches the app. The official ZIP includes `AchievementRelay.Publisher.cer`; it never includes the private key.

## Direct MSIX installation

1. Import `AchievementRelay.Publisher.cer` into **Local Computer → Trusted People** for an official package, or `AchievementRelay.Development.cer` for an explicitly downloaded test package. The recommended Setup executable performs this step automatically with administrator approval.
2. Double-click the MSIX matching the processor.
3. Select **Install**.
4. Launch Achievement Relay from Start and complete the four-step Setup flow.

## Upgrade

Run the newer `.exe` installer. The package identity preserves `%LOCALAPPDATA%\AchievementRelay`.

From official version 0.4 onward, the app checks the official GitHub Releases feed automatically. At app launch, a newer verified release downloads and opens the updater automatically. If an optional update appears while Achievement Relay is already running, it downloads and verifies quietly, then opens on the next launch; if that signed release is required, monitoring pauses and the updater opens immediately. Home and **Help & support** always show the current state and retain an explicit Retry/Install action. One failed or cancelled automatic attempt is not repeated again during the same app session, preventing update loops.

Before Setup can open, the app verifies the manifest's detached RSA signature against the publisher certificate pinned into the installed build. It also requires the release tag and signed product/package versions to agree, bounds the download, verifies its SHA-256 and exact product/file version resources, asks Windows to validate its Authenticode signature, and matches the signer against the same publisher pin. Legitimate leading or trailing padding returned by Windows for an Inno Setup version resource is removed before the numeric comparison; whitespace or text inside a version remains invalid. Monitoring pauses only when an authenticated release manifest explicitly raises the minimum supported product version above the installed version.

The verified executable opens this same installer in update mode. Update mode plays **Relay Online** through the same private 10% volume paths and exposes the same Pause/Play and direct SoundCloud controls, but skips credentials and player options. It preserves `%LOCALAPPDATA%\AchievementRelay`, the current startup preference, and whether the desktop shortcut already exists. If Setup is cancelled before deployment, the current app remains running. If package deployment fails after closing it, Setup attempts to relaunch the still-installed version.

Upgrading from 0.1.x retains the encrypted Discord webhook and preferences, then reopens Setup so a current source can be selected. Upgrading from 0.2 retains Xbox/Discord settings and cursors. Steam is enabled locally and gives every Steam account/game pair a silent first baseline, so the upgrade cannot post old Steam history.

Pre-official 0.3.x updater-test builds require one manual v0.4.0 installation from the official release page. Their temporary signing key was intentionally destroyed, so those installed builds cannot authenticate the official persistent publisher as a silent update. Running v0.4.0 Setup over the existing installation preserves encrypted connections, settings, provider baselines, pending deliveries, startup behavior, and the desktop-shortcut choice. Verified automatic updates then continue from v0.4.0 onward.

## Uninstall

Use **Settings → Apps → Installed apps → Achievement Relay → Uninstall**, or run:

```powershell
.\Uninstall.ps1
```

`Uninstall.ps1` also removes the optional desktop shortcut. Local settings remain by default. Remove settings, encrypted secrets, sync state, event ledger, and log with:

```powershell
.\Uninstall.ps1 -RemoveLocalData
```

If uninstalling directly through Windows Settings, manually remove a desktop shortcut if Windows leaves it behind. An obsolete development/test certificate may be removed from **Manage computer certificates → Trusted People → Certificates** after all packages signed by it are gone. Remove the **Achievement Relay Open Source** publisher certificate only after uninstalling Achievement Relay and deciding not to accept future updates signed by that identity.
