[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version = '0.4.0.0',

    [ValidatePattern('^$|^\d+\.\d+\.\d+$')]
    [string] $ApplicationVersion = '',

    [ValidateSet('x64', 'arm64')]
    [string[]] $Architectures = @('x64', 'arm64'),

    [string] $PfxPath,

    [string] $PfxPassword,

    [string] $TimestampUrl,

    [string] $UpdatePolicyPath,

    [switch] $AllowUntrustedDevelopmentCertificate
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repositoryRoot 'artifacts'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

if ($AllowUntrustedDevelopmentCertificate -and (-not $PfxPath -or $TimestampUrl)) {
    throw 'The untrusted-development-certificate path requires an explicit PFX and forbids production timestamping.'
}

if (-not $UpdatePolicyPath) {
    $UpdatePolicyPath = Join-Path $repositoryRoot 'release\update-policy.json'
}
$UpdatePolicyPath = [System.IO.Path]::GetFullPath($UpdatePolicyPath)
$updatePolicy = Get-Content -LiteralPath $UpdatePolicyPath -Raw |
    ConvertFrom-Json
$additionalUpdatePublisherCertificates = @($updatePolicy.additionalPublisherCertificateSha256)

Get-ChildItem -LiteralPath $outputDirectory -File -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -like "AchievementRelay_${Version}_*.msix" -or
        $_.Name -eq "AchievementRelay_${Version}_installer.zip" -or
        $_.Name -eq 'AchievementRelay_Setup.exe' -or
        $_.Name -eq 'AchievementRelay_Update.json' -or
        $_.Name -eq 'AchievementRelay_Update.sig' -or
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
            -ApplicationVersion $ApplicationVersion `
            -Configuration Release `
            -OutputDirectory $outputDirectory `
            -PfxPath $PfxPath `
            -PfxPassword $PfxPassword `
            -AdditionalUpdatePublisherCertificateSha256 $additionalUpdatePublisherCertificates `
            -TimestampUrl $TimestampUrl
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $outputDirectory -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $outputDirectory -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'docs\INSTALL.md') -Destination (Join-Path $outputDirectory 'INSTALL.md') -Force

    if (-not $ApplicationVersion) {
        $ApplicationVersion = ($Version.Split('.')[0..2] -join '.')
    }
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') `
        -Version $ApplicationVersion `
        -MsixVersion $Version `
        -PackageDirectory $outputDirectory `
        -OutputDirectory $outputDirectory `
        -PfxPath $PfxPath `
        -PfxPassword $PfxPassword `
        -TimestampUrl $TimestampUrl

    & (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') `
        -Version $ApplicationVersion `
        -PackageVersion $Version `
        -InstallerPath (Join-Path $outputDirectory 'AchievementRelay_Setup.exe') `
        -OutputPath (Join-Path $outputDirectory 'AchievementRelay_Update.json') `
        -OutputSignaturePath (Join-Path $outputDirectory 'AchievementRelay_Update.sig') `
        -PolicyPath $UpdatePolicyPath `
        -PfxPath $PfxPath `
        -PfxPassword $PfxPassword

    if (-not $publicCertificate -and -not $AllowUntrustedDevelopmentCertificate) {
        $signTool = & (Join-Path $PSScriptRoot 'Get-WindowsSdkTool.ps1') -Name 'signtool.exe'
        & $signTool verify /pa /all (Join-Path $outputDirectory 'AchievementRelay_Setup.exe')
        if ($LASTEXITCODE -ne 0) {
            throw 'The setup executable failed Authenticode verification after signing.'
        }
    }

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
