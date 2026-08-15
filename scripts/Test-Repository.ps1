[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repositoryRoot 'src\AchievementRelay.Package\AppxManifest.xml'
$manifestText = Get-Content -LiteralPath $manifestPath -Raw
$manifestText = $manifestText.Replace('__VERSION__', '0.2.1.0').Replace('__ARCHITECTURE__', 'x64')
[xml] $manifest = $manifestText

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace('f', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespaceManager.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')
$namespaceManager.AddNamespace('desktop6', 'http://schemas.microsoft.com/appx/manifest/desktop/windows10/6')
$namespaceManager.AddNamespace('virtualization', 'http://schemas.microsoft.com/appx/manifest/virtualization/windows10')
$namespaceManager.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')

$internetCapability = $manifest.SelectSingleNode('//f:Capability[@Name="internetClient"]', $namespaceManager)
$startupTask = $manifest.SelectSingleNode('//uap5:StartupTask[@TaskId="AchievementRelayStartup"]', $namespaceManager)
$application = $manifest.SelectSingleNode('//f:Application[@Executable="AchievementRelay.App.exe"]', $namespaceManager)
$unvirtualizedAppData = $manifest.SelectSingleNode('//desktop6:FileSystemWriteVirtualization[text()="disabled"]', $namespaceManager)
$excludedAppData = $manifest.SelectSingleNode('//virtualization:ExcludedDirectory[contains(text(), "AchievementRelay")]', $namespaceManager)
$unvirtualizedResources = $manifest.SelectSingleNode('//rescap:Capability[@Name="unvirtualizedResources"]', $namespaceManager)

if (-not $internetCapability) { throw 'Manifest is missing internetClient.' }
if ($manifestText.Contains('userNotificationListener')) { throw 'Obsolete notification-listener capability is still present.' }
if (-not $startupTask) { throw 'Manifest is missing AchievementRelayStartup.' }
if (-not $application) { throw 'Manifest does not launch AchievementRelay.App.exe.' }
if (-not $unvirtualizedAppData -or -not $excludedAppData -or -not $unvirtualizedResources) {
    throw 'Manifest must expose the per-user AppData folder shared by Setup and the packaged app.'
}

$requiredFiles = @(
    'README.md',
    'GETTING_STARTED.md',
    'PRIVACY.md',
    'SECURITY.md',
    'THIRD-PARTY-NOTICES.md',
    'installer\AchievementRelay.iss',
    'installer\assets\wizard-large.png',
    'scripts\Build-Installer.ps1',
    'scripts\Protect-InstallerSetup.ps1',
    'src\AchievementRelay.App\MainWindow.xaml',
    'src\AchievementRelay.App\Assets\AchievementRelay.ico',
    'src\AchievementRelay.App\Assets\RelayCommandDeck.png',
    'src\AchievementRelay.App\Assets\TrophyCup.png',
    'src\AchievementRelay.App\Assets\RadarSweep.png'
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
if (-not $installerText.Contains("GetEnv('USERPROFILE')") -or
    -not $installerText.Contains('.achievement-relay\pending-installer-setup.json')) {
    throw 'The installer handoff must use the non-virtualized per-user profile path.'
}
if (($installerText -split "`r?`n") | Where-Object { $_ -match 'Parameters.*CredentialsPage' }) {
    throw 'Installer credentials must never be placed in a process command line.'
}

$installScriptText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\Install.ps1') -Raw
$shutdownFunctionMatch = [regex]::Match(
    $installScriptText,
    '(?ms)^function Stop-AchievementRelayProcess\s*\{.*?^\}(?=\r?\n\r?\ntry\s*\{)')
$shutdownFunctionText = $shutdownFunctionMatch.Value
$shutdownCallIndex = $installScriptText.LastIndexOf('Stop-AchievementRelayProcess', [StringComparison]::Ordinal)
$packageInstallMatch = [regex]::Match(
    $installScriptText,
    '(?m)^[ \t]*Add-AppxPackage[^\r\n]*-ForceApplicationShutdown[ \t]*\r?$')
$packageInstallIndex = if ($packageInstallMatch.Success) { $packageInstallMatch.Index } else { -1 }
if (-not $shutdownFunctionMatch.Success -or
    -not $shutdownFunctionText.Contains("Get-Process -Name 'AchievementRelay.App'") -or
    -not $shutdownFunctionText.Contains('(Get-Process -Id $PID).SessionId') -or
    -not $shutdownFunctionText.Contains('Where-Object { $_.SessionId -eq $currentSessionId }') -or
    -not $shutdownFunctionText.Contains('Stop-Process') -or
    -not $shutdownFunctionText.Contains('Wait-Process -Timeout 3') -or
    $shutdownFunctionText.Contains('throw') -or
    $shutdownCallIndex -lt 0 -or $packageInstallIndex -lt 0 -or $shutdownCallIndex -gt $packageInstallIndex) {
    throw 'Setup must attempt direct shutdown, then let the AppX deployment broker close any remaining process.'
}

$protectionScriptText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\Protect-InstallerSetup.ps1') -Raw
if (-not $protectionScriptText.Contains('[Security.Cryptography.ProtectedData]::Protect')) {
    throw 'The installer handoff is not protected with Windows DPAPI.'
}
if (-not $protectionScriptText.Contains('AchievementRelay.OpenXBL.v1') -or
    -not $protectionScriptText.Contains('AchievementRelay.Webhook.v1')) {
    throw 'The installer and app secret-protection entropy contract is incomplete.'
}

$pathsText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\AppPaths.cs') -Raw
if (-not $pathsText.Contains('Environment.SpecialFolder.UserProfile') -or
    -not $pathsText.Contains('LegacyPendingInstallerSetupFile')) {
    throw 'The app must read the profile handoff path and retain legacy AppData compatibility.'
}

$importerText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\InstallerSetupImporter.cs') -Raw
$durableSaveIndex = $importerText.IndexOf('SaveAsync(storedSettings', [StringComparison]::Ordinal)
$accountRequestIndex = $importerText.IndexOf('GetAccountAsync(apiKey', [StringComparison]::Ordinal)
$discordRequestIndex = $importerText.IndexOf('webhookClient.SendAsync', [StringComparison]::Ordinal)
$handoffDeleteIndex = $importerText.IndexOf('DeletePendingSetupFiles(paths.PendingInstallerSetupFiles)', [StringComparison]::Ordinal)
if ($durableSaveIndex -lt 0 -or $accountRequestIndex -lt 0 -or $discordRequestIndex -lt 0 -or
    $handoffDeleteIndex -lt 0 -or $durableSaveIndex -gt $handoffDeleteIndex -or
    $durableSaveIndex -gt $accountRequestIndex -or $durableSaveIndex -gt $discordRequestIndex) {
    throw 'Installer secrets must be durably saved before handoff deletion or network verification.'
}

$mainWindowText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\MainWindow.xaml.cs') -Raw
$mainWindowXaml = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\MainWindow.xaml') -Raw
if (-not $mainWindowText.Contains('PopulateSecretControls()') -or
    -not $mainWindowText.Contains('ToggleSecretVisibility(') -or
    -not $mainWindowXaml.Contains('SetupXboxApiKeyRevealTextBox') -or
    -not $mainWindowXaml.Contains('SetupWebhookRevealTextBox') -or
    -not $mainWindowXaml.Contains('Content="Reveal Key"') -or
    -not $mainWindowXaml.Contains('Content="Reveal Webhook"')) {
    throw 'Stored OpenXBL and Discord secrets must appear masked and provide explicit reveal controls.'
}

$openXblParserText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\OpenXblResponseParser.cs') -Raw
if (-not $openXblParserText.Contains('"profileUsers"') -or
    -not $openXblParserText.Contains('"people"') -or
    -not $openXblParserText.Contains('"data"') -or
    -not $openXblParserText.Contains('GetAccountSetting(settings, "GameDisplayName")')) {
    throw 'The OpenXBL account parser must accept current profile envelopes and display-name fallbacks.'
}

$appProjectText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\AchievementRelay.App.csproj') -Raw
if (-not $appProjectText.Contains('THIRD-PARTY-NOTICES.md') -or
    -not $appProjectText.Contains('RelayCommandDeck.png')) {
    throw 'The packaged app must include its art notice and premium dashboard artwork.'
}

$releaseWorkflowText = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\release.yml') -Raw
if (-not $releaseWorkflowText.Contains("'.exe'")) {
    throw 'The release workflow does not publish the setup executable.'
}

$ciWorkflowText = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\ci.yml') -Raw
if (-not $ciWorkflowText.Contains('0.2.1.${{ github.run_number }}') -or
    -not $ciWorkflowText.Contains('-Version $env:TEST_MSIX_VERSION')) {
    throw 'Pull-request installers must use a monotonically increasing MSIX test revision.'
}

Write-Host 'Repository structure and package manifest checks passed.' -ForegroundColor Green
