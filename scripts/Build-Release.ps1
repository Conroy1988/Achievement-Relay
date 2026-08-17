[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version = '0.4.3.0',

    [ValidatePattern('^$|^\d+\.\d+\.\d+$')]
    [string] $ApplicationVersion = '',

    [ValidateSet('x64', 'arm64')]
    [string[]] $Architectures = @('x64', 'arm64'),

    [string] $PfxPath,

    [string] $PfxPassword,

    [string] $TimestampUrl,

    [string] $UpdatePolicyPath,

    [switch] $AllowUntrustedProjectCertificate,

    [switch] $AllowUntrustedDevelopmentCertificate
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repositoryRoot 'artifacts'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

if ($AllowUntrustedProjectCertificate -and $AllowUntrustedDevelopmentCertificate) {
    throw 'Choose either the persistent project certificate or the temporary development-certificate path, not both.'
}
if ($AllowUntrustedProjectCertificate -and (-not $PfxPath -or -not $TimestampUrl)) {
    throw 'The persistent project-certificate path requires an explicit PFX and RFC 3161 timestamp URL.'
}
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
        $_.Name -in 'AchievementRelay.Development.cer', 'AchievementRelay.Publisher.cer'
    } |
    Remove-Item -Force

$temporarySigningDirectory = $null
$publicCertificate = $null
$certificateFileName = 'AchievementRelay.Development.cer'

try {
    if (-not $PfxPath) {
        Write-Warning 'No official signing certificate was supplied. Creating a temporary development certificate for this build.'
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
    elseif ($AllowUntrustedProjectCertificate) {
        $reviewedCertificate = Join-Path $repositoryRoot 'release\AchievementRelay.Publisher.cer'
        if (-not (Test-Path -LiteralPath $reviewedCertificate -PathType Leaf)) {
            throw "The reviewed project publisher certificate is missing: $reviewedCertificate"
        }

        $resolvedPfx = (Resolve-Path -LiteralPath $PfxPath).Path
        $pfxCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $resolvedPfx,
            $PfxPassword,
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
        try {
            if (-not $pfxCertificate.HasPrivateKey -or
                $pfxCertificate.Subject -cne 'CN=Achievement Relay Open Source') {
                throw 'The protected signing PFX does not contain the expected Achievement Relay publisher identity.'
            }

            $pfxCertificateSha256 = -join @(
                [System.Security.Cryptography.SHA256]::HashData($pfxCertificate.RawData) |
                    ForEach-Object { $_.ToString('x2') }
            )
            $reviewedCertificateSha256 = (Get-FileHash -LiteralPath $reviewedCertificate -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($pfxCertificateSha256 -cne $reviewedCertificateSha256) {
                throw 'The protected signing PFX does not match release/AchievementRelay.Publisher.cer.'
            }
        }
        finally {
            $pfxCertificate.Dispose()
        }

        $certificateFileName = 'AchievementRelay.Publisher.cer'
        $publicCertificate = Join-Path $outputDirectory $certificateFileName
        Copy-Item -LiteralPath $reviewedCertificate -Destination $publicCertificate -Force
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
    $installerArguments = @{
        Version = $ApplicationVersion
        MsixVersion = $Version
        PackageDirectory = $outputDirectory
        OutputDirectory = $outputDirectory
        PfxPath = $PfxPath
        PfxPassword = $PfxPassword
        TimestampUrl = $TimestampUrl
        CertificateFileName = $certificateFileName
    }
    & (Join-Path $PSScriptRoot 'Build-Installer.ps1') @installerArguments

    & (Join-Path $PSScriptRoot 'New-UpdateManifest.ps1') `
        -Version $ApplicationVersion `
        -PackageVersion $Version `
        -InstallerPath (Join-Path $outputDirectory 'AchievementRelay_Setup.exe') `
        -OutputPath (Join-Path $outputDirectory 'AchievementRelay_Update.json') `
        -OutputSignaturePath (Join-Path $outputDirectory 'AchievementRelay_Update.sig') `
        -PolicyPath $UpdatePolicyPath `
        -PfxPath $PfxPath `
        -PfxPassword $PfxPassword

    if (-not $temporarySigningDirectory -and -not $AllowUntrustedDevelopmentCertificate) {
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
            $_.Name -in 'AchievementRelay.Development.cer', 'AchievementRelay.Publisher.cer' -or
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
