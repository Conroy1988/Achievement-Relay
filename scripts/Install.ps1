[CmdletBinding()]
param(
    [string] $ErrorFile,

    [switch] $CreateDesktopShortcut,

    [switch] $PreserveDesktopShortcut,

    [switch] $Update
)

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path

function Stop-AchievementRelayProcess {
    $currentSessionId = (Get-Process -Id $PID).SessionId
    $runningProcesses = @(
        Get-Process -Name 'AchievementRelay.App', 'AchievementRelay.SteamBridge' -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq $currentSessionId }
    )

    if ($runningProcesses.Count -eq 0) {
        return
    }

    Write-Host 'Closing the running Achievement Relay app before package deployment...'
    foreach ($runningProcess in $runningProcesses) {
        Stop-Process -Id $runningProcess.Id -Force -ErrorAction SilentlyContinue
    }

    $runningProcesses | Wait-Process -Timeout 3 -ErrorAction SilentlyContinue
    $remainingProcesses = @(
        Get-Process -Name 'AchievementRelay.App', 'AchievementRelay.SteamBridge' -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq $currentSessionId }
    )
    if ($remainingProcesses.Count -gt 0) {
        # A packaged or elevated process can outlive this best-effort user-level stop.
        # Do not abort: the AppX deployment broker receives ForceApplicationShutdown below.
        Write-Host 'Windows package deployment will close the remaining Achievement Relay instance...'
    }
}

try {
    try {
        $nativeArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
    }
    catch {
        $nativeArchitecture = $env:PROCESSOR_ARCHITEW6432
        if (-not $nativeArchitecture) {
            $nativeArchitecture = $env:PROCESSOR_ARCHITECTURE
        }
    }

    $architecture = switch -Regex ($nativeArchitecture) {
        '^Arm64$' { 'arm64'; break }
        '^(X64|AMD64)$' { 'x64'; break }
        default { throw "Unsupported Windows architecture: $nativeArchitecture" }
    }

    $package = Get-ChildItem -LiteralPath $scriptDirectory -Filter "AchievementRelay_*_${architecture}.msix" |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if (-not $package -and $architecture -eq 'arm64') {
        $package = Get-ChildItem -LiteralPath $scriptDirectory -Filter 'AchievementRelay_*_x64.msix' |
            Sort-Object Name -Descending |
            Select-Object -First 1
    }

    if (-not $package) {
        throw "No compatible Achievement Relay MSIX was found in $scriptDirectory."
    }

    $publisherCertificate = Get-ChildItem -LiteralPath $scriptDirectory -Filter 'AchievementRelay.Publisher.cer' |
        Select-Object -First 1
    $developmentCertificate = Get-ChildItem -LiteralPath $scriptDirectory -Filter 'AchievementRelay.Development.cer' |
        Select-Object -First 1
    $packageCertificate = if ($publisherCertificate) { $publisherCertificate } else { $developmentCertificate }
    if ($packageCertificate) {
        if ($publisherCertificate) {
            Write-Host 'This official open-source build uses the persistent Achievement Relay publisher certificate.' -ForegroundColor Yellow
        }
        else {
            Write-Host 'This test build uses a temporary project development certificate.' -ForegroundColor Yellow
        }

        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($packageCertificate.FullName)
        if ($publisherCertificate) {
            $expectedPublisherCertificateSha256 = '38b45563afe0a876ed676963a271c113883437d9db7ef5d6965c8226e975df69'
            $actualPublisherCertificateSha256 = (Get-FileHash `
                -LiteralPath $publisherCertificate.FullName `
                -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($actualPublisherCertificateSha256 -cne $expectedPublisherCertificateSha256 -or
                $certificate.Subject -cne 'CN=Achievement Relay Open Source') {
                throw 'The included official publisher certificate does not match the reviewed Achievement Relay identity.'
            }
        }

        $trustedCertificatePath = "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
        if (-not (Test-Path -LiteralPath $trustedCertificatePath)) {
            Write-Host 'Windows will request administrator approval once to trust the package certificate for this PC.'
            $importCommand = "Import-Certificate -FilePath `"$($packageCertificate.FullName)`" -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null"
            $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($importCommand))
            $importProcess = Start-Process `
                -FilePath 'powershell.exe' `
                -Verb RunAs `
                -ArgumentList "-NoProfile -EncodedCommand $encodedCommand" `
                -Wait `
                -PassThru
            if ($importProcess.ExitCode -ne 0) {
                throw 'The Achievement Relay package certificate was not trusted. Installation was cancelled.'
            }
        }
    }

    Write-Host "Installing $($package.Name)..."
    Stop-AchievementRelayProcess
    Add-AppxPackage -Path $package.FullName -ForceApplicationShutdown

    $installedPackage = Get-AppxPackage -Name 'Conroy.AchievementRelay' | Select-Object -First 1
    if (-not $installedPackage) {
        throw 'Windows reported success, but the Achievement Relay package could not be located.'
    }

    $desktopDirectory = [System.Environment]::GetFolderPath([System.Environment+SpecialFolder]::DesktopDirectory)
    $desktopShortcut = Join-Path $desktopDirectory 'Achievement Relay.lnk'
    if ($CreateDesktopShortcut) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($desktopShortcut)
        $shortcut.TargetPath = Join-Path $env:WINDIR 'explorer.exe'
        $shortcut.Arguments = "shell:AppsFolder\$($installedPackage.PackageFamilyName)!AchievementRelay"
        $shortcut.Description = 'Relay new Xbox and Steam achievements to Discord'
        $shortcut.IconLocation = "$(Join-Path $installedPackage.InstallLocation 'AchievementRelay.App.exe'),0"
        $shortcut.Save()
        Write-Host 'Desktop shortcut created.'
    }
    elseif (-not $PreserveDesktopShortcut -and (Test-Path -LiteralPath $desktopShortcut)) {
        Remove-Item -LiteralPath $desktopShortcut -Force
    }

    Write-Host 'Installation complete. Launching Achievement Relay...'
    Start-Process explorer.exe "shell:AppsFolder\$($installedPackage.PackageFamilyName)!AchievementRelay"
    if ($Update) {
        Write-Host 'Update complete. Existing connections, settings and achievement state were preserved.' -ForegroundColor Green
    }
    else {
        Write-Host 'Account setup will be imported, or Guided setup will open if it was skipped.' -ForegroundColor Green
    }
}
catch {
    if ($ErrorFile) {
        [System.IO.File]::WriteAllText(
            [System.IO.Path]::GetFullPath($ErrorFile),
            $_.Exception.Message,
            [System.Text.UTF8Encoding]::new($false))
    }

    if ($Update) {
        try {
            $existingPackage = Get-AppxPackage -Name 'Conroy.AchievementRelay' | Select-Object -First 1
            if ($existingPackage) {
                Start-Process explorer.exe "shell:AppsFolder\$($existingPackage.PackageFamilyName)!AchievementRelay"
            }
        }
        catch {
            # Preserve the original package-deployment error for Setup.
        }
    }
    throw
}
