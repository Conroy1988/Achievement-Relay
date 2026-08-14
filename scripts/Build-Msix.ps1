[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string] $Architecture = 'x64',

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $Version = '0.1.0.0',

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $OutputDirectory,

    [string] $PfxPath,

    [string] $PfxPassword,

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
$layoutDirectory = Join-Path $buildRoot 'layout'
$manifestTemplate = Join-Path $repositoryRoot 'src\AchievementRelay.Package\AppxManifest.xml'
$packageAssets = Join-Path $repositoryRoot 'src\AchievementRelay.Package\Assets'
$project = Join-Path $repositoryRoot 'src\AchievementRelay.App\AchievementRelay.App.csproj'
$assemblyVersion = ($Version.Split('.')[0..2] -join '.')

if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $publishDirectory, $layoutDirectory, $OutputDirectory -Force | Out-Null

Write-Host "Publishing Achievement Relay for win-$Architecture..."
dotnet publish $project `
    --configuration $Configuration `
    --runtime "win-$Architecture" `
    --self-contained true `
    --output $publishDirectory `
    -p:DebugSymbols=false `
    -p:DebugType=None `
    -p:Version=$assemblyVersion `
    -p:FileVersion=$Version `
    -p:AssemblyVersion=$Version `
    -p:ContinuousIntegrationBuild=true

if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed.'
}

Copy-Item -Path (Join-Path $publishDirectory '*') -Destination $layoutDirectory -Recurse -Force
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
