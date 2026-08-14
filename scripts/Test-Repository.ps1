[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'src\AchievementRelay.Package\AppxManifest.xml'
$manifestText = Get-Content -LiteralPath $manifestPath -Raw
$manifestText = $manifestText.Replace('__VERSION__', '0.1.1.0').Replace('__ARCHITECTURE__', 'x64')
[xml] $manifest = $manifestText

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespaceManager.AddNamespace('uap3', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/3')
$namespaceManager.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')

$listenerCapability = $manifest.SelectSingleNode('//uap3:Capability[@Name="userNotificationListener"]', $namespaceManager)
$startupTask = $manifest.SelectSingleNode('//uap5:StartupTask[@TaskId="AchievementRelayStartup"]', $namespaceManager)
$application = $manifest.SelectSingleNode('//f:Application[@Executable="AchievementRelay.App.exe"]', $namespaceManager)

if (-not $listenerCapability) { throw 'Manifest is missing userNotificationListener.' }
if (-not $startupTask) { throw 'Manifest is missing AchievementRelayStartup.' }
if (-not $application) { throw 'Manifest does not launch AchievementRelay.App.exe.' }

$requiredFiles = @(
    'README.md',
    'GETTING_STARTED.md',
    'PRIVACY.md',
    'SECURITY.md',
    'installer\AchievementRelay.iss',
    'scripts\Build-Installer.ps1',
    'src\AchievementRelay.App\MainWindow.xaml',
    'src\AchievementRelay.App\Assets\AchievementRelay.ico'
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $relativePath))) {
        throw "Required repository file is missing: $relativePath"
    }
}

$installerText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'installer\AchievementRelay.iss') -Raw
if (-not $installerText.Contains('PrivilegesRequired=lowest')) {
    throw 'The setup bootstrapper must remain per-user so the MSIX is installed for the signed-in user.'
}
if (-not $installerText.Contains('AchievementRelay_Setup')) {
    throw 'The setup bootstrapper output filename is missing.'
}

$releaseWorkflowText = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\release.yml') -Raw
if (-not $releaseWorkflowText.Contains("'.exe'")) {
    throw 'The release workflow does not publish the setup executable.'
}

Write-Host 'Repository structure and package manifest checks passed.' -ForegroundColor Green
