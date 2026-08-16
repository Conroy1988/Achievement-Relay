[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $PackageVersion,

    [string] $InstallerPath,

    [string] $PolicyPath,

    [string] $OutputPath,

    [string] $OutputSignaturePath,

    [Parameter(Mandatory)]
    [string] $PfxPath,

    [string] $PfxPassword
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

if (-not $InstallerPath) {
    $InstallerPath = Join-Path $repositoryRoot 'artifacts\AchievementRelay_Setup.exe'
}
if (-not $PolicyPath) {
    $PolicyPath = Join-Path $repositoryRoot 'release\update-policy.json'
}
if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot 'artifacts\AchievementRelay_Update.json'
}
if (-not $OutputSignaturePath) {
    $OutputSignaturePath = Join-Path $repositoryRoot 'artifacts\AchievementRelay_Update.sig'
}

if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
    throw "The updater installer is missing: $InstallerPath"
}
if ([System.IO.Path]::GetFileName($InstallerPath) -cne 'AchievementRelay_Setup.exe') {
    throw 'The updater installer must be named AchievementRelay_Setup.exe.'
}
if (-not (Test-Path -LiteralPath $PolicyPath -PathType Leaf)) {
    throw "The reviewed update policy is missing: $PolicyPath"
}

$policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
if ($policy.schemaVersion -ne 1 -or
    $policy.minimumSupportedVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw 'release/update-policy.json must use schema 1 and a numeric X.Y.Z minimumSupportedVersion.'
}
foreach ($publisherFingerprint in @($policy.additionalPublisherCertificateSha256)) {
    if ($publisherFingerprint -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'Every additional update-publisher fingerprint in the reviewed policy must be a 64-character SHA-256 value.'
    }
}

$releaseVersion = [Version]::Parse($Version)
$packageVersionValue = [Version]::Parse($PackageVersion)
$minimumSupportedVersion = [Version]::Parse($policy.minimumSupportedVersion)
foreach ($component in @(
        $releaseVersion.Major,
        $releaseVersion.Minor,
        $releaseVersion.Build,
        $packageVersionValue.Major,
        $packageVersionValue.Minor,
        $packageVersionValue.Build,
        $packageVersionValue.Revision)) {
    if ($component -gt [UInt16]::MaxValue) {
        throw 'Release and package version components must fit the Windows 0-65535 range.'
    }
}
if ($minimumSupportedVersion -gt $releaseVersion) {
    throw 'The minimum supported version cannot be newer than the release being built.'
}

$installer = Get-Item -LiteralPath $InstallerPath
if ($installer.Length -le 0 -or $installer.Length -gt 1GB) {
    throw 'The updater installer size is outside the supported 1 GiB limit.'
}
$installerVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installer.FullName)
try {
    $embeddedProductVersion = [Version]::Parse($installerVersion.ProductVersion)
    $embeddedPackageVersion = [Version]::Parse($installerVersion.FileVersion)
}
catch {
    throw "The installer product/file versions are not numeric: '$($installerVersion.ProductVersion)' and '$($installerVersion.FileVersion)'."
}
$normalizedEmbeddedProductVersion = [Version]::new(
    $embeddedProductVersion.Major,
    $embeddedProductVersion.Minor,
    [Math]::Max($embeddedProductVersion.Build, 0),
    [Math]::Max($embeddedProductVersion.Revision, 0))
$normalizedReleaseVersion = [Version]::new(
    $releaseVersion.Major,
    $releaseVersion.Minor,
    $releaseVersion.Build,
    0)
if ($normalizedEmbeddedProductVersion -ne $normalizedReleaseVersion -or
    $embeddedPackageVersion -ne $packageVersionValue) {
    throw "The installer product/file versions '$($installerVersion.ProductVersion)' and '$($installerVersion.FileVersion)' must numerically match $Version and $PackageVersion."
}

$publishedAtUtc = [DateTimeOffset]::UtcNow

$manifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    packageVersion = $PackageVersion
    minimumSupportedVersion = $policy.minimumSupportedVersion
    publishedAtUtc = $publishedAtUtc.ToString(
        'yyyy-MM-ddTHH:mm:ssZ',
        [Globalization.CultureInfo]::InvariantCulture)
    installer = [ordered]@{
        assetName = 'AchievementRelay_Setup.exe'
        sha256 = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size = $installer.Length
    }
}

$outputDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$json = $manifest | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText(
    [System.IO.Path]::GetFullPath($OutputPath),
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

$resolvedPfx = (Resolve-Path -LiteralPath $PfxPath).Path
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $resolvedPfx,
    $PfxPassword,
    [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
try {
    if (-not $certificate.HasPrivateKey -or
        $certificate.Subject -ne 'CN=Achievement Relay Open Source') {
        throw 'The update manifest requires the same private signing certificate as the MSIX publisher.'
    }

    if ($publishedAtUtc.UtcDateTime -lt $certificate.NotBefore.ToUniversalTime() -or
        $publishedAtUtc.UtcDateTime -gt $certificate.NotAfter.ToUniversalTime()) {
        throw 'The update manifest signing certificate is not currently valid.'
    }

    $codeSigningAllowed = $false
    foreach ($extension in $certificate.Extensions) {
        if ($extension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($usage in $extension.EnhancedKeyUsages) {
                if ($usage.Value -eq '1.3.6.1.5.5.7.3.3') {
                    $codeSigningAllowed = $true
                }
            }
        }
    }
    if (-not $codeSigningAllowed) {
        throw 'The update manifest signing certificate must permit code signing.'
    }

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $certificateSha256 = -join @(
            $sha256.ComputeHash($certificate.RawData) |
                ForEach-Object { $_.ToString('x2') }
        )
    }
    finally {
        $sha256.Dispose()
    }

    $installerSignature = Get-AuthenticodeSignature -LiteralPath $installer.FullName
    if (-not $installerSignature.SignerCertificate -or
        ([string] $installerSignature.Status) -in @('HashMismatch', 'NotSigned')) {
        throw 'The updater installer does not contain an intact Authenticode signature.'
    }
    $signerSha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $installerCertificateSha256 = -join @(
            $signerSha256.ComputeHash($installerSignature.SignerCertificate.RawData) |
                ForEach-Object { $_.ToString('x2') }
        )
    }
    finally {
        $signerSha256.Dispose()
    }
    if ($installerCertificateSha256 -cne $certificateSha256) {
        throw 'The update manifest and updater installer must use the same signing certificate.'
    }

    $rsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($certificate)
    if (-not $rsa -or $rsa.KeySize -lt 2048) {
        throw 'The update manifest signing certificate must contain an RSA key of at least 2048 bits.'
    }

    try {
        $manifestBytes = [System.IO.File]::ReadAllBytes([System.IO.Path]::GetFullPath($OutputPath))
        $signatureBytes = $rsa.SignData(
            $manifestBytes,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)

        $signatureEnvelope = [ordered]@{
            schemaVersion = 1
            algorithm = 'rsa-sha256-pkcs1'
            certificateSha256 = $certificateSha256
            certificate = [Convert]::ToBase64String($certificate.RawData)
            signature = [Convert]::ToBase64String($signatureBytes)
        } | ConvertTo-Json -Depth 3
        [System.IO.File]::WriteAllText(
            [System.IO.Path]::GetFullPath($OutputSignaturePath),
            $signatureEnvelope + [Environment]::NewLine,
            [System.Text.UTF8Encoding]::new($false))
    }
    finally {
        $rsa.Dispose()
    }
}
finally {
    $certificate.Dispose()
}

Write-Host "Update manifest: $OutputPath"
Write-Host "Update manifest signature: $OutputSignaturePath"
$OutputPath
$OutputSignaturePath
