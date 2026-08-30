[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64',

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version = '0.5.0.0',

    [ValidatePattern('^$|^\d+\.\d+\.\d+$')]
    [string] $ApplicationVersion = '',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputDirectory,

    [string] $PfxPath,

    [string] $PfxPassword,

    [string[]] $AdditionalUpdatePublisherCertificateSha256 = @(),

    [string] $TimestampUrl
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts'
}

$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$buildRoot = Join-Path $repositoryRoot "build\msix-$Architecture"
$publishDirectory = Join-Path $buildRoot 'publish'
$steamBridgePublishDirectory = Join-Path $buildRoot 'steam-bridge'
$layoutDirectory = Join-Path $buildRoot 'layout'
$manifestTemplate = Join-Path $repositoryRoot 'src\AchievementRelay.Package\AppxManifest.xml'
$packageAssets = Join-Path $repositoryRoot 'src\AchievementRelay.Package\Assets'
$project = Join-Path $repositoryRoot 'src\AchievementRelay.App\AchievementRelay.App.csproj'
$steamBridgeProject = Join-Path $repositoryRoot 'src\AchievementRelay.SteamBridge\AchievementRelay.SteamBridge.csproj'
if (-not $ApplicationVersion) {
    $ApplicationVersion = ($Version.Split('.')[0..2] -join '.')
}
$applicationFileVersion = "$ApplicationVersion.0"
$updatePublisherCertificateSha256 = ''
$validatedAdditionalPublisherCertificates = @(
    $AdditionalUpdatePublisherCertificateSha256 |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object {
            $normalizedFingerprint = $_.Trim().ToLowerInvariant()
            if ($normalizedFingerprint -notmatch '^[0-9a-f]{64}$') {
                throw 'Every additional update-publisher fingerprint must be a 64-character SHA-256 value.'
            }
            $normalizedFingerprint
        }
)

if ($PfxPath) {
    $resolvedSigningPfx = (Resolve-Path -LiteralPath $PfxPath).Path
    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $resolvedSigningPfx,
        $PfxPassword,
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    try {
        if (-not $certificate.HasPrivateKey) {
            throw 'The package signing PFX does not contain a private key.'
        }
        if ($certificate.Subject -ne 'CN=Achievement Relay Open Source') {
            throw "The signing certificate subject must exactly match the MSIX publisher 'CN=Achievement Relay Open Source'."
        }

        $sha256 = [System.Security.Cryptography.SHA256]::Create()
        try {
            $currentPublisherCertificateSha256 = -join @(
                $sha256.ComputeHash($certificate.RawData) |
                    ForEach-Object { $_.ToString('x2') }
            )
            $trustedPublisherCertificates = @($currentPublisherCertificateSha256) + @($validatedAdditionalPublisherCertificates)
            $updatePublisherCertificateSha256 = @(
                $trustedPublisherCertificates | Select-Object -Unique
            ) -join ';'
        }
        finally {
            $sha256.Dispose()
        }
    }
    finally {
        $certificate.Dispose()
    }
}

if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory, $steamBridgePublishDirectory, $layoutDirectory, $OutputDirectory -Force | Out-Null

Write-Host "Publishing Achievement Relay for win-$Architecture..."
dotnet publish $project `
    --configuration $Configuration `
    --runtime "win-$Architecture" `
    --self-contained true `
    --output $publishDirectory `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:Version=$ApplicationVersion `
    -p:FileVersion=$applicationFileVersion `
    -p:AssemblyVersion=$applicationFileVersion `
    "-p:UpdatePublisherCertificateSha256=$updatePublisherCertificateSha256" `
    "-p:AchievementRelayPackageVersion=$Version" `
    -p:ContinuousIntegrationBuild=true

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

Write-Host 'Publishing the isolated x64 Steamworks bridge...'
dotnet publish $steamBridgeProject `
    --configuration $Configuration `
    --output $steamBridgePublishDirectory `
    -p:Platform=x64 `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:Version=$ApplicationVersion `
    -p:FileVersion=$applicationFileVersion `
    -p:AssemblyVersion=$applicationFileVersion `
    -p:ContinuousIntegrationBuild=true

if ($LASTEXITCODE -ne 0) {
    throw 'Steam bridge publish failed.'
}

$requiredBridgeFiles = @(
    'AchievementRelay.SteamBridge.exe',
    'Facepunch.Steamworks.Win64.dll',
    'steam_api64.dll'
)
foreach ($requiredBridgeFile in $requiredBridgeFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $steamBridgePublishDirectory $requiredBridgeFile))) {
        throw "Steam bridge output is incomplete: $requiredBridgeFile is missing."
    }
}

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $layoutDirectory -Recurse -Force
$steamBridgeDestination = Join-Path $layoutDirectory 'SteamBridge'
New-Item -ItemType Directory -Path $steamBridgeDestination -Force | Out-Null
foreach ($requiredBridgeFile in $requiredBridgeFiles) {
    Copy-Item -LiteralPath (Join-Path $steamBridgePublishDirectory $requiredBridgeFile) `
        -Destination $steamBridgeDestination -Force
}
$steamBridgeConfiguration = Join-Path $steamBridgePublishDirectory 'AchievementRelay.SteamBridge.exe.config'
if (Test-Path -LiteralPath $steamBridgeConfiguration) {
    Copy-Item -LiteralPath $steamBridgeConfiguration -Destination $steamBridgeDestination -Force
}
$assetsDestination = Join-Path $layoutDirectory 'Assets'
New-Item -ItemType Directory -Path $assetsDestination -Force | Out-Null
Copy-Item -Path (Join-Path $packageAssets '*') -Destination $assetsDestination -Recurse -Force

$manifest = Get-Content -LiteralPath $manifestTemplate -Raw
$manifest = $manifest.Replace('__VERSION__', $Version).Replace('__ARCHITECTURE__', $Architecture)
$manifestPath = Join-Path $layoutDirectory 'AppxManifest.xml'
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))

$makeAppx = & (Join-Path $PSScriptRoot 'Get-WindowsSdkTool.ps1') -Name 'makeappx.exe'
$packagePath = Join-Path $OutputDirectory "AchievementRelay_${Version}_${Architecture}.msix"
if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

Write-Host "Creating $packagePath..."
& $makeAppx pack /d $layoutDirectory /p $packagePath /o
if ($LASTEXITCODE -ne 0) {
    throw 'MakeAppx failed to create the package.'
}

if ($PfxPath) {
    $resolvedPfx = (Resolve-Path -LiteralPath $PfxPath).Path
    $signTool = & (Join-Path $PSScriptRoot 'Get-WindowsSdkTool.ps1') -Name 'signtool.exe'
    Write-Host 'Signing the MSIX with SHA-256...'
    $signArguments = @('sign', '/fd', 'SHA256', '/f', $resolvedPfx)
    if ($PfxPassword) {
        $signArguments += @('/p', $PfxPassword)
    }
    if ($TimestampUrl) {
        $signArguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }
    $signArguments += $packagePath
    & $signTool @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'SignTool failed to sign the package.'
    }
}
else {
    Write-Warning 'The MSIX is unsigned and cannot be installed until it is signed.'
}

Write-Host "Created: $packagePath"
$packagePath
