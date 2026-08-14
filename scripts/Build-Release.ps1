[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version = '0.1.1.0',

    [ValidateSet('x64', 'arm64')]
    [string[]] $Architectures = @('x64', 'arm64'),

    [string] $PfxPath,

    [string] $PfxPassword,

    [string] $TimestampUrl
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repositoryRoot 'artifacts'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

Get-ChildItem -LiteralPath $outputDirectory -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -like "AchievementRelay_${Version}_*.msix" -or
        $_.Name -eq "AchievementRelay_${Version}_installer.zip" -or
        $_.Name -eq 'AchievementRelay_Setup.exe' -or
        $_.Name -eq 'AchievementRelay.Development.cer'
    } |
    Remove-Item -Force

$temporarySigningDirectory = $null
$publicCertificate = $null

try {
    if (-not $PfxPath) {
        Write-Warning 'No production signing certificate was supplied. Creating a development certificate for this build.'
        $temporarySigningDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("AchievementRelay-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temporarySigningDirectory -Force | Out-Null
        $PfxPath = Join-Path $temporarySigningDirectory 'AchievementRelay.Development.pfx'
        $publicCertificate = Join-Path $outputDirectory 'AchievementRelay.Development.cer'
        $PfxPassword = [Guid]::NewGuid().ToString('N')
        & (Join-Path $PSScriptRoot 'New-DevelopmentCertificate.ps1') `
            -PfxPath $PfxPath `
            -CerPath $publicCertificate `
            -Password $PfxPassword
    }

    foreach ($architecture in $Architectures) {
        & (Join-Path $PSScriptRoot 'Build-Msix.ps1') `
            -Architecture $architecture `
            -Version $Version `
            -Configuration Release `
            -OutputDirectory $outputDirectory `
            -PfxPath $PfxPath `
            -PfxPassword $PfxPassword `
            -TimestampUrl $TimestampUrl
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $outputDirectory -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $outputDirectory -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\INSTALL.md') -Destination (Join-Path $outputDirectory 'INSTALL.md') -Force

    $applicationVersion = ($Version.Split('.')[0..2] -join '.')
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') `
        -Version $applicationVersion `
        -MsixVersion $Version `
        -PackageDirectory $outputDirectory `
        -OutputDirectory $outputDirectory `
        -PfxPath $PfxPath `
        -PfxPassword $PfxPassword `
        -TimestampUrl $TimestampUrl

    $archivePath = Join-Path $outputDirectory "AchievementRelay_${Version}_installer.zip"
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    $releaseFiles = Get-ChildItem -LiteralPath $outputDirectory -File |
        Where-Object {
            $_.Name -like "AchievementRelay_${Version}_*.msix" -or
            $_.Name -eq 'AchievementRelay.Development.cer' -or
            $_.Name -in 'Install.ps1', 'Uninstall.ps1', 'INSTALL.md'
        }
    Compress-Archive -LiteralPath $releaseFiles.FullName -DestinationPath $archivePath -CompressionLevel Optimal
    Write-Host "Release bundle: $archivePath"
}
finally {
    if ($temporarySigningDirectory -and (Test-Path -LiteralPath $temporarySigningDirectory)) {
        Remove-Item -LiteralPath $temporarySigningDirectory -Recurse -Force
    }
}
