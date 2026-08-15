[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'src\AchievementRelay.Package\AppxManifest.xml'
$manifestText = Get-Content -LiteralPath $manifestPath -Raw
$manifestText = $manifestText.Replace('__VERSION__', '0.2.0.0').Replace('__ARCHITECTURE__', 'x64')
[xml] $manifest = $manifestText

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespaceManager.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')
$namespaceManager.AddNamespace('desktop6', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/6')
$namespaceManager.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')

$internetCapability = $manifest.SelectSingleNode('//f:Capability[@Name="internetClient"]', $namespaceManager)
$startupTask = $manifest.SelectSingleNode('//uap5:StartupTask[@TaskId="AchievementRelayStartup"]', $namespaceManager)
$application = $manifest.SelectSingleNode('//f:Application[@Executable="AchievementRelay.App.exe"]', $namespaceManager)
$unvirtualizedAppData = $manifest.SelectSingleNode('//desktop6:FileSystemWriteVirtualization[text()="disabled"]', $namespaceManager)
$unvirtualizedResources = $manifest.SelectSingleNode('//rescap:Capability[@Name="unvirtualizedResources"]', $namespaceManager)

if (-not $internetCapability) { throw 'Manifest is missing internetClient.' }
if ($manifestText.Contains('userNotificationListener')) { throw 'Obsolete notification-listener capability is still present.' }
if (-not $startupTask) { throw 'Manifest is missing AchievementRelayStartup.' }
if (-not $application) { throw 'Manifest does not launch AchievementRelay.App.exe.' }
if (-not $unvirtualizedAppData -or -not $unvirtualizedResources) {
    throw 'Manifest must expose the per-user AppData folder shared by Setup and the packaged app.'
}

$requiredFiles = @(
    'README.md',
    'GETTING_STARTED.md',
    'PRIVACY.md',
    'SECURITY.md',
    'installer\AchievementRelay.iss',
    'installer\assets\wizard-large.png',
    'scripts\Build-Installer.ps1',
    'scripts\Protect-InstallerSetup.ps1',
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
if (-not $installerText.Contains('pending-installer-setup.json')) {
    throw 'The setup bootstrapper is missing the optional secure setup handoff.'
}
if (-not $installerText.Contains('desktopicon')) {
    throw 'The setup bootstrapper is missing the desktop-shortcut choice.'
}
if (-not $installerText.Contains('SetEnvironmentVariable')) {
    throw 'The setup bootstrapper is missing the short-lived credential handoff.'
}
if (($installerText -split "`r?`n") | Where-Object { $_ -match 'Parameters.*CredentialsPage' }) {
    throw 'Installer credentials must never be placed in a process command line.'
}

$protectionScriptText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\Protect-InstallerSetup.ps1') -Raw
if (-not $protectionScriptText.Contains('[Security.Cryptography.ProtectedData]::Protect')) {
    throw 'The installer handoff is not protected with Windows DPAPI.'
}
if (-not $protectionScriptText.Contains('AchievementRelay.OpenXBL.v1') -or
    -not $protectionScriptText.Contains('AchievementRelay.Webhook.v1')) {
    throw 'The installer and app secret-protection entropy contract is incomplete.'
}

$releaseWorkflowText = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\release.yml') -Raw
if (-not $releaseWorkflowText.Contains("'.exe'")) {
    throw 'The release workflow does not publish the setup executable.'
}

Write-Host 'Repository structure and package manifest checks passed.' -ForegroundColor Green
