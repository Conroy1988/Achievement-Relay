[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '0.4.0',

    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $MsixVersion = '0.4.0.0',

    [string] $PackageDirectory,

    [string] $OutputDirectory,

    [string] $IsccPath,

    [string] $PfxPath,

    [string] $PfxPassword,

    [string] $TimestampUrl,

    [switch] $ForceUpdateMode
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installerScript = Join-Path $repositoryRoot 'installer\AchievementRelay.iss'

if (-not $PackageDirectory) {
    $PackageDirectory = Join-Path $repositoryRoot 'artifacts'
}
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts'
}

$PackageDirectory = [System.IO.Path]::GetFullPath($PackageDirectory)
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

foreach ($architecture in 'x64', 'arm64') {
    $packagePath = Join-Path $PackageDirectory "AchievementRelay_${MsixVersion}_${architecture}.msix"
    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "The $architecture MSIX is missing: $packagePath"
    }
}

if (-not $IsccPath) {
    $isccCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($isccCommand) {
        $IsccPath = $isccCommand.Source
    }
}

if (-not $IsccPath) {
    $isccCandidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    $IsccPath = $isccCandidates |
        Where-Object { $_ -and (Test-Path -LiteralPath $_) } |
        Select-Object -First 1
}

if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    throw 'Inno Setup 6 was not found. Install Inno Setup 6 or pass -IsccPath.'
}

$installerPath = Join-Path $OutputDirectory 'AchievementRelay_Setup.exe'
if (Test-Path -LiteralPath $installerPath) {
    Remove-Item -LiteralPath $installerPath -Force
}

$compilerArguments = @(
    "/DAppVersion=$Version",
    "/DMsixVersion=$MsixVersion",
    "/DPackageDirectory=$PackageDirectory",
    "/DOutputDirectory=$OutputDirectory",
    "/DRepositoryRoot=$repositoryRoot"
)
if ($ForceUpdateMode) {
    $compilerArguments += '/DForceUpdateMode=1'
}
$compilerArguments += $installerScript

Write-Host 'Building the single-file Windows installer...'
& $IsccPath @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Inno Setup failed to build AchievementRelay_Setup.exe.'
}
if (-not (Test-Path -LiteralPath $installerPath)) {
    throw "Inno Setup reported success, but the installer was not found: $installerPath"
}

if ($PfxPath) {
    $resolvedPfx = (Resolve-Path -LiteralPath $PfxPath).Path
    $signTool = & (Join-Path $PSScriptRoot 'Get-WindowsSdkTool.ps1') -Name 'signtool.exe'
    $signArguments = @('sign', '/fd', 'SHA256', '/f', $resolvedPfx)
    if ($PfxPassword) {
        $signArguments += @('/p', $PfxPassword)
    }
    if ($TimestampUrl) {
        $signArguments += @('/tr', $TimestampUrl, '/td', 'SHA256')
    }
    $signArguments += $installerPath

    Write-Host 'Signing the setup executable with SHA-256...'
    & $signTool @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw 'SignTool failed to sign AchievementRelay_Setup.exe.'
    }
}
else {
    Write-Warning 'AchievementRelay_Setup.exe is unsigned because no PFX was supplied.'
}

Write-Host "Created: $installerPath"
$installerPath
