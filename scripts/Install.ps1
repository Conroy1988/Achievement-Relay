[CmdletBinding()]
param(
    [string] $ErrorFile,

    [switch] $CreateDesktopShortcut
)

$ErrorActionPreference = 'Stop'
$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path

function Stop-AchievementRelayProcess {
    $currentSessionId = (Get-Process -Id $PID).SessionId
    $runningProcesses = @(
        Get-Process -Name 'AchievementRelay.App' -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq $currentSessionId }
    )

    if ($runningProcesses.Count -eq 0) {
        return
    }

    Write-Host 'Closing the running Achievement Relay app before the update...'
    foreach ($runningProcess in $runningProcesses) {
        Stop-Process -Id $runningProcess.Id -Force -ErrorAction SilentlyContinue
    }

    $runningProcesses | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    $remainingProcesses = @(
        Get-Process -Name 'AchievementRelay.App' -ErrorAction SilentlyContinue |
            Where-Object { $_.SessionId -eq $currentSessionId }
    )
    if ($remainingProcesses.Count -gt 0) {
        throw 'Achievement Relay is still running. Exit it from the notification area and run Setup again.'
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

    $developmentCertificate = Get-ChildItem -LiteralPath $scriptDirectory -Filter 'AchievementRelay.Development.cer' |
        Select-Object -First 1
    if ($developmentCertificate) {
        Write-Host 'This alpha build uses a project development certificate.' -ForegroundColor Yellow
        $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($developmentCertificate.FullName)
        $trustedCertificatePath = "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
        if (-not (Test-Path -LiteralPath $trustedCertificatePath)) {
            Write-Host 'Windows will request administrator approval once to trust the package certificate for this PC.'
            $importCommand = "Import-Certificate -FilePath `"$($developmentCertificate.FullName)`" -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null"
            $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($importCommand))
            $importProcess = Start-Process `
                -FilePath 'powershell.exe' `
                -Verb RunAs `
                -ArgumentList "-NoProfile -EncodedCommand $encodedCommand" `
                -Wait `
                -PassThru
            if ($importProcess.ExitCode -ne 0) {
                throw 'The development certificate was not trusted. Installation was cancelled.'
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
        $shortcut.Description = 'Relay Xbox achievements to Discord'
        $shortcut.IconLocation = "$(Join-Path $installedPackage.InstallLocation 'AchievementRelay.App.exe'),0"
        $shortcut.Save()
        Write-Host 'Desktop shortcut created.'
    }
    elseif (Test-Path -LiteralPath $desktopShortcut) {
        Remove-Item -LiteralPath $desktopShortcut -Force
    }

    Write-Host 'Installation complete. Launching Achievement Relay...'
    Start-Process explorer.exe "shell:AppsFolder\$($installedPackage.PackageFamilyName)!AchievementRelay"
    Write-Host 'Account setup will be imported, or Guided setup will open if it was skipped.' -ForegroundColor Green
}
catch {
    if ($ErrorFile) {
        [System.IO.File]::WriteAllText(
            [System.IO.Path]::GetFullPath($ErrorFile),
            $_.Exception.Message,
            [System.Text.UTF8Encoding]::new($false))
    }
    throw
}
