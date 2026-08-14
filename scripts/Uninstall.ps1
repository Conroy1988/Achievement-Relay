[CmdletBinding()]
param(
    [switch] $RemoveLocalData
)

$ErrorActionPreference = 'Stop'
$packages = Get-AppxPackage -Name 'Conroy.AchievementRelay'
if (-not $packages) {
    Write-Host 'Achievement Relay is not installed for this Windows account.'
    return
}

$packages | Remove-AppxPackage
Write-Host 'Achievement Relay was uninstalled.'

if ($RemoveLocalData) {
    $dataDirectory = Join-Path $env:LOCALAPPDATA 'AchievementRelay'
    if (Test-Path -LiteralPath $dataDirectory) {
        Remove-Item -LiteralPath $dataDirectory -Recurse -Force
        Write-Host 'Local settings, logs and the encrypted webhook were removed.'
    }
}
else {
    Write-Host 'Local settings were kept. Run .\Uninstall.ps1 -RemoveLocalData to remove them too.'
}
