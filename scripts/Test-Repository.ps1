[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Get-WcagRelativeLuminance {
    param([Parameter(Mandatory)][string] $Color)

    $hex = $Color.Trim().TrimStart([char] '#')
    if ($hex.Length -eq 8) {
        $hex = $hex.Substring(2)
    }
    if ($hex.Length -ne 6) {
        throw "WCAG contrast checks require a six- or eight-digit hex color. Received: $Color"
    }

    $components = @(foreach ($offset in 0, 2, 4) {
        [Convert]::ToInt32($hex.Substring($offset, 2), 16) / 255.0
    })
    $linear = @($components | ForEach-Object {
        if ($_ -le 0.04045) {
            $_ / 12.92
        }
        else {
            [Math]::Pow(($_ + 0.055) / 1.055, 2.4)
        }
    })

    return (0.2126 * $linear[0]) + (0.7152 * $linear[1]) + (0.0722 * $linear[2])
}

function Assert-WcagContrast {
    param(
        [Parameter(Mandatory)][string] $Name,
        [Parameter(Mandatory)][string] $Foreground,
        [Parameter(Mandatory)][string] $Background,
        [Parameter(Mandatory)][double] $Minimum
    )

    $foregroundLuminance = Get-WcagRelativeLuminance $Foreground
    $backgroundLuminance = Get-WcagRelativeLuminance $Background
    $lighter = [Math]::Max($foregroundLuminance, $backgroundLuminance)
    $darker = [Math]::Min($foregroundLuminance, $backgroundLuminance)
    $ratio = ($lighter + 0.05) / ($darker + 0.05)
    if ($ratio -lt $Minimum) {
        $roundedRatio = [Math]::Round($ratio, 2)
        throw "$Name has a contrast ratio of ${roundedRatio}:1; ${Minimum}:1 is required."
    }
}

function Get-ThemeColor {
    param(
        [Parameter(Mandatory)][xml] $Document,
        [Parameter(Mandatory)][string] $Key
    )

    $xamlNamespace = 'http://schemas.microsoft.com/winfx/2006/xaml'
    $node = @($Document.SelectNodes('//*[local-name()="Color"]')) |
        Where-Object { $_.GetAttribute('Key', $xamlNamespace) -eq $Key } |
        Select-Object -First 1
    if (-not $node) {
        throw "Theme color is missing: $Key"
    }

    return $node.InnerText.Trim()
}

$manifestPath = Join-Path $repositoryRoot 'src\AchievementRelay.Package\AppxManifest.xml'
$manifestText = Get-Content -LiteralPath $manifestPath -Raw
$manifestText = $manifestText.Replace('__VERSION__', '0.5.0.0').Replace('__ARCHITECTURE__', 'x64')
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
    'CHANGELOG.md',
    'GETTING_STARTED.md',
    'PRIVACY.md',
    'SECURITY.md',
    'THIRD-PARTY-NOTICES.md',
    '.github\ISSUE_TEMPLATE\config.yml',
    'NuGet.config',
    'installer\AchievementRelay.iss',
    'installer\assets\wizard-large.png',
    'installer\assets\CRNY - Relay Online.mp3',
    'scripts\Build-Installer.ps1',
    'scripts\New-UpdateManifest.ps1',
    'scripts\Protect-InstallerSetup.ps1',
    'release\update-policy.json',
    'release\AchievementRelay.Publisher.cer',
    'release\publisher-certificate.json',
    'release\live-update-test-policy.json',
    '.github\workflows\live-update-test.yml',
    'docs\LIVE-UPDATE-TEST.md',
    'docs\RELEASE-NOTES-0.4.0.md',
    'docs\RELEASE-NOTES-0.4.1.md',
    'docs\RELEASE-NOTES-0.4.2.md',
    'docs\RELEASE-NOTES-0.4.3.md',
    'docs\RELEASE-NOTES-0.5.0.md',
    'docs\ACCESSIBILITY.md',
    'docs\images\achievement-relay-banner.png',
    'docs\images\achievement-relay-interface.png',
    'docs\images\achievement-relay-social-preview.png',
    'src\AchievementRelay.SteamBridge\AchievementRelay.SteamBridge.csproj',
    'src\AchievementRelay.SteamBridge\Program.cs',
    'src\AchievementRelay.Core\Services\SteamAchievementDeltaDetector.cs',
    'src\AchievementRelay.Core\Services\SteamRarityResponseParser.cs',
    'src\AchievementRelay.Core\Services\RgbaPngEncoder.cs',
    'src\AchievementRelay.Core\Services\RelayRarityClassifier.cs',
    'src\AchievementRelay.Core\Services\XboxPlatformClassifier.cs',
    'src\AchievementRelay.Core\Models\RelayRarityTier.cs',
    'src\AchievementRelay.Core\Services\UpdatePolicy.cs',
    'src\AchievementRelay.Core\Services\XboxDeliveryWindowPolicy.cs',
    'src\AchievementRelay.Core\Models\UpdateManifest.cs',
    'tests\AchievementRelay.App.Tests\AchievementRelay.App.Tests.csproj',
    'tests\AchievementRelay.App.Tests\Program.cs',
    'third_party\Facepunch.Steamworks.LICENSE.txt',
    'third_party\packages\Facepunch.Steamworks.2.5.2.nupkg',
    'src\AchievementRelay.App\MainWindow.xaml',
    'src\AchievementRelay.App\Services\AppUpdateService.cs',
    'src\AchievementRelay.App\Services\InstallerTrustVerifier.cs',
    'src\AchievementRelay.App\Services\AchievementArtworkClient.cs',
    'src\AchievementRelay.App\Services\DiscordAchievementPostComposer.cs',
    'src\AchievementRelay.App\Services\DiscordCollectorCardRenderer.cs',
    'src\AchievementRelay.Core\Services\UpdateManifestSignatureVerifier.cs',
    'src\AchievementRelay.App\Assets\AchievementRelay.ico',
    'assets\brand\achievement-relay-icon-source.png',
    'src\AchievementRelay.App\Assets\RelayCommandDeck.png',
    'src\AchievementRelay.App\Assets\TrophyCup.png',
    'src\AchievementRelay.App\Assets\RadarSweep.png',
    'src\AchievementRelay.App\Assets\Xbox.png',
    'src\AchievementRelay.App\Assets\Steam.png',
    'src\AchievementRelay.App\Assets\Discord.png',
    'assets\third-party\platform-icons\xbox.svg',
    'assets\third-party\platform-icons\steam.svg',
    'assets\third-party\platform-icons\discord.svg'
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
if (-not $installerText.Contains('OpenXBL API key (optional') -or
    -not $installerText.Contains('(Length(ApiKey) > 0)')) {
    throw 'The installer must support a Steam-only setup without requiring an OpenXBL key.'
}
if (-not $installerText.Contains("GetEnv('USERPROFILE')") -or
    -not $installerText.Contains('.achievement-relay\pending-installer-setup.json')) {
    throw 'The installer handoff must use the non-virtualized per-user profile path.'
}
if (($installerText -split "`r?`n") | Where-Object { $_ -match 'Parameters.*CredentialsPage' }) {
    throw 'Installer credentials must never be placed in a process command line.'
}

$soundtrackPath = Join-Path $repositoryRoot 'installer\assets\CRNY - Relay Online.mp3'
$soundtrackHash = (Get-FileHash -LiteralPath $soundtrackPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($soundtrackHash -ne '3581211124af4f328a7e6c27d4b726cc0ead0a88b0751bf1113d867272c4b182') {
    throw 'The installer soundtrack does not match the original CRNY - Relay Online upload.'
}

if (-not $installerText.Contains('CRNY - Relay Online.mp3"; Flags: dontcopy noencryption') -or
    -not $installerText.Contains("CreateOleObject('WMPlayer.OCX')") -or
    -not $installerText.Contains('Settings.volume := 10') -or
    -not $installerText.Contains("Settings.setMode('loop', True)") -or
    -not $installerText.Contains('MusicPlayer.controls.pause') -or
    -not $installerText.Contains('MusicPlayer.controls.play') -or
    -not $installerText.Contains("setaudio ' + MusicAlias + ' volume to 100") -or
    -not $installerText.Contains("play ' + MusicAlias + ' repeat") -or
    -not $installerText.Contains("pause ' + MusicAlias") -or
    -not $installerText.Contains("resume ' + MusicAlias") -or
    -not $installerText.Contains('StartInstallerMusic(not UpdateMode)') -or
    -not $installerText.Contains('MarkMusicReadyMuted') -or
    -not $installerText.Contains('no audio backend will start until Play music is selected') -or
    -not $installerText.Contains('procedure DeinitializeSetup') -or
    -not $installerText.Contains('https://soundcloud.com/daniel-conroy-224318319/crny-relay-online')) {
    throw 'The installer soundtrack must remain temporary, local, limited to 10%, controllable, looped, and linked to CRNY on SoundCloud through independent primary and fallback playback paths.'
}

if (-not $installerText.Contains("'{param:UPDATE|0}'") -or
    -not $installerText.Contains('#ifdef ForceUpdateMode') -or
    -not $installerText.Contains('UPGRADE THE ACHIEVEMENT RELAY') -or
    -not $installerText.Contains('(PageID = wpSelectTasks)') -or
    -not $installerText.Contains("Parameters := Parameters + ' -Update -PreserveDesktopShortcut'")) {
    throw 'The verified updater must reuse the branded installer while skipping onboarding and preserving user choices.'
}
if (-not $installerText.Contains('administrator approval once to trust the included public package certificate')) {
    throw 'The first-run installer must disclose the one-time package-certificate trust step before installation.'
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
    -not $shutdownFunctionText.Contains("Get-Process -Name 'AchievementRelay.App', 'AchievementRelay.SteamBridge'") -or
    -not $shutdownFunctionText.Contains('(Get-Process -Id $PID).SessionId') -or
    -not $shutdownFunctionText.Contains('Where-Object { $_.SessionId -eq $currentSessionId }') -or
    -not $shutdownFunctionText.Contains('Stop-Process') -or
    -not $shutdownFunctionText.Contains('Wait-Process -Timeout 3') -or
    $shutdownFunctionText.Contains('throw') -or
    $shutdownCallIndex -lt 0 -or $packageInstallIndex -lt 0 -or $shutdownCallIndex -gt $packageInstallIndex) {
    throw 'Setup must attempt direct shutdown, then let the AppX deployment broker close any remaining process.'
}
if (-not $installScriptText.Contains('[switch] $PreserveDesktopShortcut') -or
    -not $installScriptText.Contains('[switch] $Update') -or
    -not $installScriptText.Contains('-not $PreserveDesktopShortcut') -or
    -not $installScriptText.Contains("Filter 'AchievementRelay.Publisher.cer'") -or
    -not $installScriptText.Contains('if ($publisherCertificate) { $publisherCertificate } else { $developmentCertificate }') -or
    -not $installScriptText.Contains('38b45563afe0a876ed676963a271c113883437d9db7ef5d6965c8226e975df69') -or
    -not $installScriptText.Contains('Cert:\LocalMachine\TrustedPeople')) {
    throw 'Package updates must preserve the existing desktop shortcut and relaunch the installed app.'
}

$protectionScriptText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\Protect-InstallerSetup.ps1') -Raw
if (-not $protectionScriptText.Contains('[Security.Cryptography.ProtectedData]::Protect')) {
    throw 'The installer handoff is not protected with Windows DPAPI.'
}
if (-not $protectionScriptText.Contains('AchievementRelay.OpenXBL.v1') -or
    -not $protectionScriptText.Contains('AchievementRelay.Webhook.v1')) {
    throw 'The installer and app secret-protection entropy contract is incomplete.'
}
if (-not $protectionScriptText.Contains("if ([string]::IsNullOrWhiteSpace(`$apiKey))") -or
    -not $protectionScriptText.Contains("protectedOpenXblApiKey = `$protectedApiKey")) {
    throw 'The encrypted installer handoff must safely represent an omitted optional OpenXBL key.'
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
if (-not $importerText.Contains('var hasApiKey = !string.IsNullOrWhiteSpace(pendingApiKey)') -or
    -not $importerText.Contains('SteamEnabled = currentSettings.SteamEnabled') -or
    -not $importerText.Contains('var providerReady = nextSettings.SteamEnabled') -or
    -not $importerText.Contains('var completed = webhookResult.Success && providerReady')) {
    throw 'Installer import must complete a Steam-only setup after Discord verification.'
}

$mainWindowText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\MainWindow.xaml.cs') -Raw
$mainWindowXaml = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\MainWindow.xaml') -Raw
$appThemeXaml = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\App.xaml') -Raw
$appStartupText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\App.xaml.cs') -Raw
$mainWindowConstructor = [regex]::Match(
    $mainWindowText,
    '(?ms)^    public MainWindow\(AppServices services, AppSettings settings\).*?^    \}')
if (-not $mainWindowText.Contains('PopulateSecretControls()') -or
    -not $mainWindowText.Contains('ToggleSecretVisibility(') -or
    -not $mainWindowXaml.Contains('SetupXboxApiKeyRevealTextBox') -or
    -not $mainWindowXaml.Contains('SetupWebhookRevealTextBox') -or
    -not $mainWindowXaml.Contains('Content="Reveal Key"') -or
    -not $mainWindowXaml.Contains('Content="Reveal Webhook"')) {
    throw 'Stored OpenXBL and Discord secrets must appear masked and provide explicit reveal controls.'
}
if (-not $mainWindowText.Contains('SteamMonitorCoordinator.StatusChanged') -or
    -not $mainWindowText.Contains('RefreshSteam_Click') -or
    -not $mainWindowText.Contains('await _services.SteamMonitorCoordinator.StartAsync();') -or
    -not $mainWindowText.Contains('SteamMonitoringPhase.Monitoring') -or
    -not $mainWindowText.Contains('Steam phase:') -or
    -not $mainWindowXaml.Contains('SetupSteamEnabledCheckBox') -or
    -not $mainWindowXaml.Contains('SettingsSteamEnabledCheckBox') -or
    -not $mainWindowXaml.Contains('x:Name="SteamStatusText"')) {
    throw 'Steam setup, settings, refresh and live dashboard status controls are incomplete.'
}
if (-not $mainWindowXaml.Contains('x:Name="HomePrimaryActionButton"') -or
    -not $mainWindowXaml.Contains('x:Name="SetupSteps"') -or
    -not $mainWindowXaml.Contains('Step 1 of 4') -or
    -not $mainWindowXaml.Contains('Click="SetupNextFromSources_Click"') -or
    -not $mainWindowXaml.Contains('Click="SetupNextFromDiscord_Click"') -or
    -not $mainWindowXaml.Contains('Click="HomePrimaryAction_Click"') -or
    -not $mainWindowXaml.Contains('Assets/Xbox.png') -or
    -not $mainWindowXaml.Contains('Assets/Steam.png') -or
    -not $mainWindowXaml.Contains('Assets/Discord.png') -or
    -not $mainWindowXaml.Contains('Help &amp; support') -or
    -not $mainWindowXaml.Contains('Community &amp; support') -or
    -not $mainWindowXaml.Contains('Click="OpenCommunityDiscord_Click"') -or
    -not $mainWindowXaml.Contains('Support on Ko-fi') -or
    -not $mainWindowText.Contains('https://discord.gg/3ZdXhYjgDm') -or
    -not $mainWindowText.Contains('ApplyHomeState(') -or
    -not $mainWindowText.Contains('OpenSetupAtRecommendedStep()') -or
    -not $mainWindowText.Contains('UpdateNavigationState()')) {
    throw 'The simplified home, four-step setup, platform identity, support, and navigation experience is incomplete.'
}
if (-not [regex]::IsMatch(
        $mainWindowXaml,
        'x:Name="MainTabs"[\s\S]*?SelectedIndex="0"[\s\S]*?<TabControl\.Resources>') -or
    -not $mainWindowConstructor.Success -or
    -not $mainWindowConstructor.Value.Contains('NavigateTo(0);') -or
    -not $mainWindowText.Contains('public void ShowHome()') -or
    -not $mainWindowText.Contains('if (MainTabs.SelectedIndex < 0)') -or
    ([regex]::Matches($appStartupText, '_mainWindow\.ShowHome\(\);')).Count -lt 2) {
    throw 'Every visible app launch, including the updater relaunch, must select and render the Home page instead of an empty command surface.'
}
if (-not $appThemeXaml.Contains('<Color x:Key="BackgroundColor">#07090A</Color>') -or
    -not $appThemeXaml.Contains('<Color x:Key="TextColor">#F4F1EB</Color>') -or
    -not $appThemeXaml.Contains('<Color x:Key="AccentColor">#D72B32</Color>') -or
    -not $appThemeXaml.Contains('x:Key="AccentTextBrush"') -or
    -not $appThemeXaml.Contains('x:Key="SuccessBrush"')) {
    throw 'The command-red theme must preserve its readable ink, bone, crimson, and semantic success hierarchy.'
}
if (-not $appThemeXaml.Contains('x:Key="ContentTabControl"') -or
    -not $appThemeXaml.Contains('<Setter Property="TextElement.Foreground" Value="{StaticResource TextBrush}" />') -or
    -not $appThemeXaml.Contains('<Style TargetType="ListBox">') -or
    -not $appThemeXaml.Contains('<Style TargetType="ListBoxItem">') -or
    -not $appThemeXaml.Contains('<Style TargetType="ScrollViewer">') -or
    -not $appThemeXaml.Contains('<Style TargetType="ScrollBar">') -or
    -not $appThemeXaml.Contains('<Setter Property="Foreground" Value="{StaticResource DisabledTextBrush}" />') -or
    -not $mainWindowXaml.Contains('<Grid Background="{StaticResource WindowSurfaceBrush}">') -or
    ([regex]::Matches($mainWindowXaml, 'Style="\{StaticResource ContentTabControl\}"')).Count -ne 2) {
    throw 'Every app surface and reusable control must remain explicitly dark, readable, and independent of native TabControl colors.'
}

$undersizedText = [regex]::Matches($mainWindowXaml, 'FontSize="(?<size>[0-9]+(?:\.[0-9]+)?)"') |
    Where-Object { [double] $_.Groups['size'].Value -lt 11 }
if ($undersizedText) {
    throw 'The main interface must not use explicit text smaller than 11 device-independent pixels.'
}

[xml] $appThemeDocument = $appThemeXaml
$themeColors = @{}
@(
    'BackgroundColor',
    'PanelRaisedColor',
    'BorderColor',
    'ControlBorderColor',
    'TextColor',
    'MutedTextColor',
    'AccentColor',
    'AccentHoverColor',
    'AccentTextColor',
    'AccentOutlineColor',
    'DisabledSurfaceColor',
    'DisabledTextColor',
    'SuccessColor',
    'WarningColor',
    'ErrorColor',
    'DiscordColor',
    'DiscordOutlineColor',
    'KoFiOutlineColor',
    'XboxOutlineColor',
    'SteamOutlineColor'
) | ForEach-Object {
    $themeColors[$_] = Get-ThemeColor -Document $appThemeDocument -Key $_
}

foreach ($textColor in @('TextColor', 'MutedTextColor', 'AccentTextColor', 'SuccessColor', 'WarningColor', 'ErrorColor')) {
    Assert-WcagContrast -Name "$textColor on raised cards" `
        -Foreground $themeColors[$textColor] `
        -Background $themeColors['PanelRaisedColor'] `
        -Minimum 4.5
}
Assert-WcagContrast -Name 'Primary button text' `
    -Foreground '#FFFFFF' `
    -Background $themeColors['AccentColor'] `
    -Minimum 4.5
Assert-WcagContrast -Name 'Discord button text' `
    -Foreground '#FFFFFF' `
    -Background $themeColors['DiscordColor'] `
    -Minimum 4.5
Assert-WcagContrast -Name 'Button hover text' `
    -Foreground $themeColors['TextColor'] `
    -Background $themeColors['AccentHoverColor'] `
    -Minimum 4.5
Assert-WcagContrast -Name 'Disabled control text' `
    -Foreground $themeColors['DisabledTextColor'] `
    -Background $themeColors['DisabledSurfaceColor'] `
    -Minimum 4.5
Assert-WcagContrast -Name 'Card boundary' `
    -Foreground $themeColors['BorderColor'] `
    -Background $themeColors['PanelRaisedColor'] `
    -Minimum 3.0
Assert-WcagContrast -Name 'Input and button boundary' `
    -Foreground $themeColors['ControlBorderColor'] `
    -Background '#090B0C' `
    -Minimum 3.0
foreach ($outlineColor in @('AccentOutlineColor', 'DiscordOutlineColor', 'KoFiOutlineColor', 'XboxOutlineColor', 'SteamOutlineColor')) {
    Assert-WcagContrast -Name "$outlineColor on raised cards" `
        -Foreground $themeColors[$outlineColor] `
        -Background $themeColors['PanelRaisedColor'] `
        -Minimum 3.0
}

$openXblParserText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\OpenXblResponseParser.cs') -Raw
if (-not $openXblParserText.Contains('"profileUsers"') -or
    -not $openXblParserText.Contains('"people"') -or
    -not $openXblParserText.Contains('"data"') -or
    -not $openXblParserText.Contains('inheritedXuid') -or
    -not $openXblParserText.Contains('GetAccountSetting(settings, "GameDisplayName")')) {
    throw 'The OpenXBL account parser must accept current profile envelopes and display-name fallbacks.'
}

$openXblClientText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\OpenXblClient.cs') -Raw
$openXblBudgetText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\OpenXblRequestBudget.cs') -Raw
$relayCoordinatorText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\RelayCoordinator.cs') -Raw
if (-not $openXblClientText.Contains('new("https://api.xbl.io/")') -or
    -not $openXblClientText.Contains('"api/v2/account"') -or
    -not $openXblClientText.Contains('"v2/account"') -or
    $openXblClientText.Contains('https://xbl.io/api/v2/')) {
    throw 'OpenXBL requests must stay on the provider current https://api.xbl.io/ origin.'
}
if (-not $openXblClientText.Contains('"api/v2/player/titleHistory"') -or
    -not $openXblClientText.Contains('"v2/player/titleHistory"') -or
    $openXblClientText.Contains('"api/v2/player/titleHistory/{xuid}"') -or
    $openXblClientText.Contains('"v2/player/titleHistory/{xuid}"') -or
    -not $openXblClientText.Contains('"api/v2/achievements/player/{xuid}"') -or
    -not $openXblClientText.Contains('"api/v2/achievements/player/{xuid}/title/{titleId}"') -or
    -not $openXblClientText.Contains('"api/v2/achievements/x360/{xuid}/title/{titleId}"') -or
    -not $openXblClientText.Contains('"api/v2/achievements/player/{xuid}/{titleId}"') -or
    -not $openXblClientText.Contains('"api/v2/achievements/title/{titleId}"') -or
    -not $openXblClientText.Contains('achievements.Length == Math.Max(0, expectedUnlockedCount)') -or
    -not $openXblClientText.Contains('_preferredTitleAchievementRouteTemplates[titleId] = routeTemplate') -or
    -not $openXblClientText.Contains('HttpStatusCode.BadRequest') -or
    -not $openXblParserText.Contains('TryGetProperty(item, "unlocked"') -or
    -not $openXblClientText.Contains('EndpointProbeRetryAfter = TimeSpan.FromMinutes(5)') -or
    -not $openXblClientText.Contains('_preferredTitleProgressRouteTemplate = routeTemplate')) {
    throw 'OpenXBL polling must negotiate complete modern and Xbox 360 detail responses, cache success per title, and back off failed probes.'
}
if (-not $openXblClientText.Contains('MaximumTitleDetailRequestsPerOperation = 12') -or
    -not $openXblClientText.Contains('X-RateLimit-Remaining') -or
    -not $openXblClientText.Contains('RateLimitFallbackRetryAfter = TimeSpan.FromHours(1)') -or
    -not $openXblBudgetText.Contains('OpenXblRequestPriority.Background') -or
    -not $relayCoordinatorText.Contains('isBackgroundWork ? OpenXblRequestPriority.Background : OpenXblRequestPriority.Essential') -or
    -not $openXblBudgetText.Contains('LocalHourlySafetyCeiling = 120') -or
    -not $openXblBudgetText.Contains('GetBackgroundReserve()') -or
    -not $openXblBudgetText.Contains('ObserveProviderWindow') -or
    -not $openXblBudgetText.Contains('ObserveRateLimited')) {
    throw 'OpenXBL requests must obey provider headers, preserve a rolling-hour reserve, and cap each multi-page operation.'
}

$deltaDetectorText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\AchievementDeltaDetector.cs') -Raw
$deliveryWindowPolicyText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\XboxDeliveryWindowPolicy.cs') -Raw
$syncWorkText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Models\XboxTitleSyncWork.cs') -Raw
$syncStateText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\XboxSyncStateStore.cs') -Raw
if (-not $openXblParserText.Contains('DateTimeOffset? unlockedAt = null') -or
    -not $openXblParserText.Contains('UnlockTimeEstimated = unlockedAt is null') -or
    -not $deltaDetectorText.Contains('previousAchievementIds') -or
    -not $deltaDetectorText.Contains('deliveryEpochUtc') -or
    -not $deltaDetectorText.Contains('allowUntimestampedIdentityDelta') -or
    -not $deltaDetectorText.Contains('provenLive') -or
    $deltaDetectorText.Contains('previousReportedGamerscore') -or
    $deltaDetectorText.Contains('AttributeByCountAndGamerscore') -or
    $deltaDetectorText.Contains('FindUniqueGamerscoreCombination') -or
    -not $deliveryWindowPolicyText.Contains('MinimumContinuity = TimeSpan.FromMinutes(10)') -or
    -not $deliveryWindowPolicyText.Contains('elapsed < TimeSpan.Zero || elapsed > continuityLimit') -or
    -not $syncWorkText.Contains('LiveDeliveryEpochUtc') -or
    -not $syncWorkText.Contains('AllowsUntimestampedDelivery') -or
    -not $syncStateText.Contains('CurrentSchemaVersion = 7') -or
    -not $syncStateText.Contains('sourceSchemaVersion') -or
    -not $syncStateText.Contains('sourceSchemaVersion >= 6') -or
    -not $syncStateText.Contains('hasValidLiveEvidence') -or
    -not $syncStateText.Contains('UnlockedAchievementIds') -or
    -not $syncStateText.Contains('XboxPlatformClassifier.NormalizeDevices') -or
    -not $syncWorkText.Contains('string[] Devices') -or
    -not $syncStateText.Contains('PendingTitles') -or
    -not $syncStateText.Contains('LastBackgroundWorkUtc') -or
    -not $syncStateText.Contains('Math.Max(') -or
    -not $relayCoordinatorText.Contains('QueueTitleWork(') -or
    -not $relayCoordinatorText.Contains('XboxSyncWorkPlanner.SelectNext') -or
    -not $relayCoordinatorText.Contains('BackgroundWorkInterval = TimeSpan.FromMinutes(15)') -or
    -not $relayCoordinatorText.Contains('ShouldPauseAllOpenXblWork') -or
    -not $relayCoordinatorText.Contains('if (selectedWork is null && backgroundWorkDue)') -or
    -not $relayCoordinatorText.Contains('AchievementDeltaDetector.Detect') -or
    -not $relayCoordinatorText.Contains('XboxDeliveryWindowPolicy.Resolve') -or
    -not $relayCoordinatorText.Contains('selectedWork.LiveDeliveryEpochUtc ?? deliveryWindow.EpochUtc') -or
    -not $relayCoordinatorText.Contains('selectedWork.AllowsUntimestampedDelivery') -or
    -not $relayCoordinatorText.Contains('prevent cross-device reposts') -or
    -not $relayCoordinatorText.Contains('var hydrationTitle =') -or
    -not $relayCoordinatorText.Contains('Nothing historical was sent to Discord') -or
    $relayCoordinatorText.Contains('no new timestamped achievement is available yet')) {
    throw 'Achievement polling must silently reconcile inactive-device history and post only unlocks proven inside a continuous live-delivery window.'
}

$discordClientText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\DiscordWebhookClient.cs') -Raw
if (-not $openXblParserText.Contains('ParseContinuationToken') -or
    -not $openXblClientText.Contains('MaximumContinuationPages') -or
    -not $openXblClientText.Contains('achievements/title/{escapedTitleId}') -or
    -not $discordClientText.Contains('.Where(item => !item.StartsWith("wait="') -or
    -not $discordClientText.Contains('.Append("wait=true")')) {
    throw 'Provider paging and confirmed Discord webhook delivery contracts are incomplete.'
}

$appProjectText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\AchievementRelay.App.csproj') -Raw
if (-not $appProjectText.Contains('THIRD-PARTY-NOTICES.md') -or
    -not $appProjectText.Contains('Facepunch.Steamworks.LICENSE.txt') -or
    -not $appProjectText.Contains('RelayCommandDeck.png')) {
    throw 'The packaged app must include its art notice and premium dashboard artwork.'
}

$steamDeltaText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\SteamAchievementDeltaDetector.cs') -Raw
$steamMonitorText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\SteamMonitorCoordinator.cs') -Raw
$steamRarityText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\SteamRarityClient.cs') -Raw
$steamRarityParserText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\SteamRarityResponseParser.cs') -Raw
$steamBridgeText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.SteamBridge\Program.cs') -Raw
$discordPayloadText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\DiscordWebhookPayloadFactory.cs') -Raw
$buildMsixText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\Build-Msix.ps1') -Raw
$rarityClassifierText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\RelayRarityClassifier.cs') -Raw
$platformClassifierText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\XboxPlatformClassifier.cs') -Raw
$collectorCardRendererText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\DiscordCollectorCardRenderer.cs') -Raw
$artworkClientText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\AchievementArtworkClient.cs') -Raw
$postComposerText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\DiscordAchievementPostComposer.cs') -Raw
$deliveryServiceText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\AchievementDeliveryService.cs') -Raw
$appServicesText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\AppServices.cs') -Raw
$appSmokeTestsText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'tests\AchievementRelay.App.Tests\Program.cs') -Raw
if (-not $rarityClassifierText.Contains('< 3 => RelayRarityTier.Platinum') -or
    -not $rarityClassifierText.Contains('< 10 => RelayRarityTier.Gold') -or
    -not $rarityClassifierText.Contains('< 25 => RelayRarityTier.Silver') -or
    -not $rarityClassifierText.Contains('RelayRarityTier.Unranked') -or
    -not $rarityClassifierText.Contains('double.IsFinite(percentage)') -or
    -not $rarityClassifierText.Contains('percentage is < 0 or > 100') -or
    -not $platformClassifierText.Contains('return classifications.Count == 1 ? classifications.Single() : null') -or
    -not $platformClassifierText.Contains('return ClassifyToken(earnedPlatform)') -or
    -not $platformClassifierText.Contains('return "Xbox PC"') -or
    -not $platformClassifierText.Contains('return "Xbox Console"') -or
    -not $platformClassifierText.Contains('return "Xbox 360"')) {
    throw 'Collector Card rarity tiers and Xbox platform labels must use validated, fail-generic evidence.'
}
if (-not $collectorCardRendererText.Contains('CardWidth = 1200') -or
    -not $collectorCardRendererText.Contains('CardHeight = 675') -or
    -not $collectorCardRendererText.Contains('achievement-relay-card.png') -or
    -not $collectorCardRendererText.Contains('MaximumCardBytes') -or
    -not $collectorCardRendererText.Contains('DrawFallbackPattern') -or
    -not $collectorCardRendererText.Contains('DrawTierEmblem') -or
    -not $collectorCardRendererText.Contains('RelayRarityTier.Unranked') -or
    -not $artworkClientText.Contains('AllowAutoRedirect = false') -or
    -not $artworkClientText.Contains('MaximumArtworkBytes') -or
    -not $artworkClientText.Contains('CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)') -or
    -not $artworkClientText.Contains('timeout.CancelAfter(ArtworkRequestTimeout)') -or
    -not $artworkClientText.Contains('candidate.Scheme != Uri.UriSchemeHttps') -or
    -not $artworkClientText.Contains('AllowedImageHosts') -or
    -not $artworkClientText.Contains('"images-eds-ssl.xboxlive.com"') -or
    -not $artworkClientText.Contains('"store-images.s-microsoft.com"') -or
    -not $artworkClientText.Contains('"cdn.akamai.steamstatic.com"') -or
    -not $artworkClientText.Contains('IsSupportedRasterImage')) {
    throw 'Collector Cards must remain bounded, locally rendered and complete without optional provider artwork.'
}
if (-not $appServicesText.Contains('AchievementPostComposer = new DiscordAchievementPostComposer') -or
    -not $appServicesText.Contains('AchievementPostComposer,') -or
    -not $deliveryServiceText.Contains('postComposer.ComposeAsync') -or
    -not $deliveryServiceText.Contains('post.AttachmentBytes') -or
    -not $deliveryServiceText.Contains('post.AttachmentFileName') -or
    -not $deliveryServiceText.Contains('post.AttachmentContentType') -or
    -not $mainWindowText.Contains('_services.AchievementPostComposer.ComposeAsync(sample') -or
    -not $postComposerText.Contains('CreateLegacyPost') -or
    -not $discordPayloadText.Contains('embed["image"]') -or
    -not $discordPayloadText.Contains('embed["thumbnail"]') -or
    -not $discordPayloadText.Contains('payload["attachments"]') -or
    -not $discordPayloadText.Contains('CreateAttachmentDescription')) {
    throw 'Collector Card composition, attachment delivery, sample posting and legacy thumbnail fallback must remain connected end to end.'
}
if (-not $appSmokeTestsText.Contains('Collector Card PNG contract') -or
    -not $appSmokeTestsText.Contains('Collector Card artwork composition') -or
    -not $appSmokeTestsText.Contains('Collector Card long text safety') -or
    -not $appSmokeTestsText.Contains('Collector Card tier emblems are distinct') -or
    -not $appSmokeTestsText.Contains('BinaryPrimitives.ReadInt32BigEndian') -or
    -not $appSmokeTestsText.Contains('HashTierEmblem')) {
    throw 'Collector Card renderer smoke coverage must retain output, artwork, text-safety and distinct-tier checks.'
}
$steamMutationPattern = '(?m)\b(?:achievement|new\s+Achievement\s*\([^)]*\))\s*\.\s*(?:Trigger|Clear)\s*\(|\bSteamUserStats\s*\.\s*(?:StoreStats|ResetAll)\s*\('
if (-not $steamDeltaText.Contains('Merely appearing unlocked is always history') -or
    -not $steamDeltaText.Contains('Unlock timestamps are display metadata') -or
    -not $steamDeltaText.Contains('previousUnlockedApiNames is null') -or
    -not $steamDeltaText.Contains('transitionIds.Contains(item.ApiName)') -or
    $steamDeltaText.Contains('item.UnlockedAt is') -or
    -not $steamMonitorText.Contains('previous?.UnlockedAchievementApiNames') -or
    -not $steamMonitorText.Contains('PendingAchievementApiNames') -or
    -not $steamMonitorText.Contains('GetDeliveryBackoff') -or
    -not $steamMonitorText.Contains('Steam observation processing failed safely') -or
    -not $steamMonitorText.Contains('during {processingStage}') -or
    -not $steamMonitorText.Contains('throw new InvalidDataException("Steam bridge returned unreadable JSON.")') -or
    -not $steamMonitorText.Contains('Keep draining the trusted helper') -or
    -not $steamMonitorText.Contains('var teardownToken = CancellationToken.None') -or
    -not $steamMonitorText.Contains('Steam returned an invalid observation timestamp') -or
    $steamMonitorText.Contains('.Where(unlockedIds.Contains)') -or
    -not $steamMonitorText.Contains('.Where(item => pendingIds.Contains(item.ApiName))') -or
    -not $steamMonitorText.Contains('accounts[snapshot.SteamId]') -or
    -not $steamMonitorText.Contains('await PersistStateAsync();') -or
    -not $steamMonitorText.Contains('SteamAchievementDeltaDetector.Detect') -or
    -not $steamMonitorText.Contains('OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)') -or
    -not $steamMonitorText.Contains('InitialObservationTimeout = TimeSpan.FromSeconds(45)') -or
    -not $steamMonitorText.Contains('SteamMonitoringPhase.EstablishingBaseline') -or
    -not $steamMonitorText.Contains('CheckBridgeStartupTimeout(current, now)') -or
    -not $steamBridgeText.Contains('if (!schemaReady || names.Length == 0)') -or
    -not $steamBridgeText.Contains('stableSchemaReads >= 3') -or
    -not $steamBridgeText.Contains('StatsRequestTimeout = TimeSpan.FromSeconds(20)') -or
    -not $steamBridgeText.Contains('StatsRefreshInterval = TimeSpan.FromSeconds(10)') -or
    -not $steamBridgeText.Contains('new Friend(steamId).RequestUserStatsAsync()') -or
    -not $steamBridgeText.Contains('forceRequest: true') -or
    -not $steamBridgeText.Contains('localPlayer.GetAchievement(apiName, false)') -or
    -not $steamBridgeText.Contains('CompleteStatsRequest(request, statsReady, StatsRequestTimeout') -or
    -not $steamBridgeText.Contains('Status = "stats-ready"') -or
    -not $steamBridgeText.Contains('SteamUserStats.OnUserStatsReceived += statsReceived') -or
    -not $steamBridgeText.Contains('SteamUserStats.OnUserStatsUnloaded += statsUnloaded') -or
    -not $steamBridgeText.Contains('SteamUserStats.OnAchievementProgress += achievementProgress') -or
    -not $steamBridgeText.Contains('SteamUserStats.OnAchievementProgress -= achievementProgress') -or
    -not $steamBridgeText.Contains('currentProgress == 0 && maximumProgress == 0') -or
    -not $steamBridgeText.Contains('sessionTransitions.Add(achievement.Identifier)') -or
    -not $steamBridgeText.Contains('sessionTransitions.Add(apiName)') -or
    -not $steamBridgeText.Contains('sessionTransitions.Clear()') -or
    -not $steamBridgeText.Contains('Interlocked.Increment(ref statsGeneration)') -or
    -not $steamBridgeText.Contains('currentStatsGeneration != observedStatsGeneration') -or
    -not $steamBridgeText.Contains('getStatsGeneration() != currentStatsGeneration') -or
    -not $steamBridgeText.Contains('!string.Equals(observedSteamId, currentSteamId, StringComparison.Ordinal)') -or
    -not $steamBridgeText.Contains('steamId.Equals(SteamClient.SteamId)') -or
    -not $steamBridgeText.Contains('if (!statsReady.IsSet)') -or
    -not $steamBridgeText.Contains('statsWereReady') -or
    -not $steamBridgeText.Contains('InitialSnapshot = previous == null') -or
    -not $steamBridgeText.Contains('TransitionedApiNames = newlyUnlocked') -or
    -not $steamBridgeText.Contains('previous.TryGetValue(item.Key, out var wasUnlocked)') -or
    -not $steamBridgeText.Contains('MaximumSnapshotIconBytes') -or
    -not $steamBridgeText.Contains('Convert.ToBase64String(icon.Value.Data)') -or
    -not $steamBridgeText.Contains('IconByteCount') -or
    -not $steamBridgeText.Contains('AQIDBA==') -or
    -not $steamBridgeText.Contains('icon.Value.Width > 512') -or
    -not $steamBridgeText.Contains('SteamUserStats.Achievements') -or
    -not $steamRarityText.Contains('Rarity is optional enrichment') -or
    -not $steamRarityText.Contains('SteamRarityResponseParser.Parse') -or
    -not $steamRarityParserText.Contains('JsonValueKind.String') -or
    -not $steamRarityParserText.Contains('NumberStyles.Float') -or
    -not $discordPayloadText.Contains('NormalizeUnicode') -or
    -not $discordPayloadText.Contains('[Get the relay]') -or
    -not $discordPayloadText.Contains('https://github.com/Conroy1988/Achievement-Relay') -or
    $steamBridgeText -match $steamMutationPattern) {
    throw 'Steam monitoring must require stable complete snapshots, live transition proof, and durable pending delivery before relaying changes.'
}
$persistIndex = $steamMonitorText.IndexOf('await PersistStateAsync();')
$deliverIndex = $steamMonitorText.IndexOf('deliveryService.DeliverAsync')
if ($persistIndex -lt 0 -or $deliverIndex -lt 0 -or $persistIndex -ge $deliverIndex) {
    throw 'Steam transitions must be persisted before any Discord delivery attempt.'
}
if (-not $buildMsixText.Contains('AchievementRelay.SteamBridge.csproj') -or
    -not $buildMsixText.Contains("'Facepunch.Steamworks.Win64.dll'") -or
    -not $buildMsixText.Contains("'steam_api64.dll'") -or
    -not $buildMsixText.Contains("Join-Path `$layoutDirectory 'SteamBridge'") -or
    -not $buildMsixText.Contains('Copy-Item -LiteralPath (Join-Path $steamBridgePublishDirectory $requiredBridgeFile)')) {
    throw 'Every MSIX architecture must include the isolated x64 Steamworks bridge and its native dependency.'
}

$steamworksPackage = Join-Path $repositoryRoot 'third_party\packages\Facepunch.Steamworks.2.5.2.nupkg'
$steamworksPackageHash = (Get-FileHash -LiteralPath $steamworksPackage -Algorithm SHA256).Hash.ToLowerInvariant()
if ($steamworksPackageHash -ne '11e12d1b34d22a6c7ed6b5f70fd145f4794fc9b4c5fc9c5b380eb73b02b7571e') {
    throw 'The vendored official Facepunch.Steamworks 2.5.2 package hash does not match the reviewed release asset.'
}

$releaseWorkflowText = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\release.yml') -Raw
if (-not $releaseWorkflowText.Contains("'.exe'") -or
    -not $releaseWorkflowText.Contains("'.json'") -or
    -not $releaseWorkflowText.Contains("'.sig'") -or
    -not $releaseWorkflowText.Contains('AchievementRelay.SteamBridge.exe --self-test') -or
    -not $releaseWorkflowText.Contains('Official self-updating releases require the persistent project signing PFX') -or
    -not $releaseWorkflowText.Contains('AllowUntrustedProjectCertificate = $true') -or
    -not $releaseWorkflowText.Contains('Cert:\LocalMachine\TrustedPeople') -or
    -not $releaseWorkflowText.Contains('http://timestamp.digicert.com') -or
    -not $releaseWorkflowText.Contains('AchievementRelay.Publisher.cer') -or
    -not $releaseWorkflowText.Contains('default: v0.5.0') -or
    -not $releaseWorkflowText.Contains('--export-collector-card-preview') -or
    -not $releaseWorkflowText.Contains('AchievementRelay_CollectorCard_Preview.png') -or
    -not $releaseWorkflowText.Contains('Start-Process') -or
    -not $releaseWorkflowText.Contains('$previewProcess.ExitCode') -or
    -not $releaseWorkflowText.Contains('$previewWidth -ne 1200') -or
    -not $releaseWorkflowText.Contains('$previewHeight -ne 675') -or
    -not $releaseWorkflowText.Contains('tests\AchievementRelay.App.Tests') -or
    -not $releaseWorkflowText.Contains('publish_release:') -or
    -not $releaseWorkflowText.Contains('Retain signed release candidate') -or
    -not $releaseWorkflowText.Contains('RELEASE-NOTES-$version.md')) {
    throw 'The release workflow must verify the Steam bridge and publish a persistently signed updater plus manifest.'
}

$publisherCertificatePath = Join-Path $repositoryRoot 'release\AchievementRelay.Publisher.cer'
$publisherCertificateMetadata = Get-Content `
    -LiteralPath (Join-Path $repositoryRoot 'release\publisher-certificate.json') `
    -Raw | ConvertFrom-Json
$publisherCertificateSha256 = (Get-FileHash -LiteralPath $publisherCertificatePath -Algorithm SHA256).Hash.ToLowerInvariant()
$expectedPublisherCertificateSha256 = '38b45563afe0a876ed676963a271c113883437d9db7ef5d6965c8226e975df69'
$publisherCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($publisherCertificatePath)
try {
    $publisherCodeSigningAllowed = $false
    $publisherIsCertificateAuthority = $false
    $publisherAllowsDigitalSignature = $false
    foreach ($extension in $publisherCertificate.Extensions) {
        if ($extension -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]) {
            foreach ($usage in $extension.EnhancedKeyUsages) {
                if ($usage.Value -eq '1.3.6.1.5.5.7.3.3') {
                    $publisherCodeSigningAllowed = $true
                }
            }
        }
        elseif ($extension -is [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]) {
            $publisherIsCertificateAuthority = $extension.CertificateAuthority
        }
        elseif ($extension -is [System.Security.Cryptography.X509Certificates.X509KeyUsageExtension]) {
            $publisherAllowsDigitalSignature = ($extension.KeyUsages -band
                [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature) -ne 0
        }
    }

    $publisherRsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($publisherCertificate)
    try {
        if ($publisherCertificate.HasPrivateKey -or
            $publisherCertificate.Subject -cne 'CN=Achievement Relay Open Source' -or
            $publisherCertificate.Issuer -cne $publisherCertificate.Subject -or
            -not $publisherRsa -or $publisherRsa.KeySize -ne 3072 -or
            -not $publisherCodeSigningAllowed -or
            -not $publisherAllowsDigitalSignature -or
            $publisherIsCertificateAuthority -or
            $publisherCertificateSha256 -cne $expectedPublisherCertificateSha256 -or
            $publisherCertificateMetadata.schemaVersion -ne 1 -or
            $publisherCertificateMetadata.subject -cne $publisherCertificate.Subject -or
            $publisherCertificateMetadata.certificateSha256 -cne $publisherCertificateSha256 -or
            $publisherCertificateMetadata.serialNumber -cne $publisherCertificate.SerialNumber -or
            $publisherCertificateMetadata.notBeforeUtc -cne $publisherCertificate.NotBefore.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') -or
            $publisherCertificateMetadata.notAfterUtc -cne $publisherCertificate.NotAfter.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') -or
            $publisherCertificateMetadata.publicKey -cne 'RSA-3072' -or
            $publisherCertificateMetadata.enhancedKeyUsage -cne '1.3.6.1.5.5.7.3.3' -or
            $publisherCertificateMetadata.trustModel -cne 'persistent-project-self-signed') {
            throw 'The reviewed persistent publisher certificate or its public metadata is invalid.'
        }
    }
    finally {
        if ($publisherRsa) {
            $publisherRsa.Dispose()
        }
    }
}
finally {
    $publisherCertificate.Dispose()
}

$officialUpdatePolicy = Get-Content -LiteralPath (Join-Path $repositoryRoot 'release\update-policy.json') -Raw |
    ConvertFrom-Json
$appProjectText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\AchievementRelay.App.csproj') -Raw
$bridgeProjectText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.SteamBridge\AchievementRelay.SteamBridge.csproj') -Raw
$buildReleaseText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\Build-Release.ps1') -Raw
$buildInstallerText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\Build-Installer.ps1') -Raw
$discordClientVersionText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\DiscordWebhookClient.cs') -Raw
$openXblClientVersionText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\OpenXblClient.cs') -Raw
$steamRarityClientVersionText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\SteamRarityClient.cs') -Raw
if ($officialUpdatePolicy.schemaVersion -ne 1 -or
    $officialUpdatePolicy.minimumSupportedVersion -cne '0.4.0' -or
    @($officialUpdatePolicy.additionalPublisherCertificateSha256).Count -ne 0 -or
    -not $appProjectText.Contains('<Version>0.5.0</Version>') -or
    -not $appProjectText.Contains('<FileVersion>0.5.0.0</FileVersion>') -or
    -not $appProjectText.Contains('<AssemblyVersion>0.5.0.0</AssemblyVersion>') -or
    -not $bridgeProjectText.Contains('<Version>0.5.0</Version>') -or
    -not $bridgeProjectText.Contains('<FileVersion>0.5.0.0</FileVersion>') -or
    -not $bridgeProjectText.Contains('<AssemblyVersion>0.5.0.0</AssemblyVersion>') -or
    -not $installerText.Contains('#define AppVersion "0.5.0"') -or
    -not $buildReleaseText.Contains("[string] `$Version = '0.5.0.0'") -or
    -not $buildMsixText.Contains("[string] `$Version = '0.5.0.0'") -or
    -not $buildInstallerText.Contains("[string] `$Version = '0.5.0'") -or
    -not $buildInstallerText.Contains("[string] `$MsixVersion = '0.5.0.0'") -or
    -not $mainWindowXaml.Contains('Text="Version 0.5.0"') -or
    -not $mainWindowText.Contains('?? "0.5.0"') -or
    -not $discordClientVersionText.Contains('ProductInfoHeaderValue("AchievementRelay", "0.5.0")') -or
    -not $openXblClientVersionText.Contains('ProductInfoHeaderValue("AchievementRelay", "0.5.0")') -or
    -not $steamRarityClientVersionText.Contains('ProductInfoHeaderValue("AchievementRelay", "0.5.0")')) {
    throw 'The v0.5.0 application and Steam bridge must retain the official v0.4.0 update baseline.'
}

$liveUpdatePolicy = Get-Content -LiteralPath (Join-Path $repositoryRoot 'release\live-update-test-policy.json') -Raw |
    ConvertFrom-Json
$liveUpdateWorkflowText = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\live-update-test.yml') -Raw
if ($liveUpdatePolicy.schemaVersion -ne 1 -or
    $liveUpdatePolicy.minimumSupportedVersion -cne '0.3.2' -or
    @($liveUpdatePolicy.additionalPublisherCertificateSha256).Count -ne 0 -or
    -not $liveUpdateWorkflowText.Contains('ref: ${{ github.sha }}') -or
    -not $liveUpdateWorkflowText.Contains('BASELINE_VERSION: 0.3.1') -or
    -not $liveUpdateWorkflowText.Contains('BASELINE_PACKAGE_VERSION: 0.3.1.1') -or
    -not $liveUpdateWorkflowText.Contains('TARGET_VERSION: 0.3.2') -or
    -not $liveUpdateWorkflowText.Contains('TARGET_PACKAGE_VERSION: 0.3.2.0') -or
    -not $liveUpdateWorkflowText.Contains('-ForceUpdateMode:$ForceUpdateMode') -or
    -not $liveUpdateWorkflowText.Contains('release\live-update-test-policy.json') -or
    -not $liveUpdateWorkflowText.Contains('AllowUntrustedDevelopmentCertificate = $true') -or
    -not $liveUpdateWorkflowText.Contains('AchievementRelay_Baseline_Setup.exe') -or
    -not $liveUpdateWorkflowText.Contains('gh release create') -or
    -not $liveUpdateWorkflowText.Contains('/releases/latest') -or
    -not $liveUpdateWorkflowText.Contains('--latest')) {
    throw 'The controlled updater test must build its corrected automatic-update baseline and signed required target, then verify GitHub latest-stable discovery.'
}

$updatePolicyText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\UpdatePolicy.cs') -Raw
$appUpdateText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\AppUpdateService.cs') -Raw
$installerTrustText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.App\Services\InstallerTrustVerifier.cs') -Raw
$manifestTrustText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'src\AchievementRelay.Core\Services\UpdateManifestSignatureVerifier.cs') -Raw
$newUpdateManifestText = Get-Content -LiteralPath (Join-Path $repositoryRoot 'scripts\New-UpdateManifest.ps1') -Raw
if (-not $updatePolicyText.Contains('minimum supported version') -or
    -not $updatePolicyText.Contains('UpdateRequirement.Required') -or
    -not $updatePolicyText.Contains('currentPackageVersion') -or
    -not $appUpdateText.Contains('api.github.com/repos/{Owner}/{Repository}/releases/latest') -or
    -not $appUpdateText.Contains('SHA256.HashDataAsync') -or
    -not $appUpdateText.Contains('ParseOfficialAssetUri') -or
    -not $appUpdateText.Contains('InstallerTrustVerifier.Verify') -or
    -not $appUpdateText.Contains('UpdateManifestSignatureVerifier.Verify') -or
    -not $appUpdateText.Contains('ManifestSignatureBase64') -or
    -not $appUpdateText.Contains('UpdatePolicy.MatchesInstallerVersionResource') -or
    -not $updatePolicyText.Contains('normalizedProductVersion = productVersion?.Trim()') -or
    -not $updatePolicyText.Contains('SelectAutomaticAction') -or
    -not $installerTrustText.Contains('WinVerifyTrust') -or
    -not $installerTrustText.Contains('AchievementRelay.UpdatePublisherCertificateSha256') -or
    -not $manifestTrustText.Contains('rsa-sha256-pkcs1') -or
    -not $buildMsixText.Contains('-p:UpdatePublisherCertificateSha256=') -or
    -not $buildMsixText.Contains('-p:AchievementRelayPackageVersion=') -or
    -not $buildReleaseText.Contains('New-UpdateManifest.ps1') -or
    -not $buildReleaseText.Contains('-PackageVersion $Version') -or
    -not $buildReleaseText.Contains('-PolicyPath $UpdatePolicyPath') -or
    -not $buildReleaseText.Contains('[switch] $AllowUntrustedProjectCertificate') -or
    -not $buildReleaseText.Contains('release\AchievementRelay.Publisher.cer') -or
    -not $buildReleaseText.Contains('does not match release/AchievementRelay.Publisher.cer') -or
    -not $buildReleaseText.Contains('$AllowUntrustedDevelopmentCertificate -and (-not $PfxPath -or $TimestampUrl)') -or
    -not $newUpdateManifestText.Contains('$embeddedPackageVersion -ne $packageVersionValue') -or
    -not $newUpdateManifestText.Contains('$embeddedProductVersionText = ([string] $installerVersion.ProductVersion).Trim()') -or
    -not $newUpdateManifestText.Contains('$installerCertificateSha256 -cne $certificateSha256') -or
    -not $mainWindowText.Contains('EnforceRequiredUpdateAsync') -or
    -not $mainWindowText.Contains('TryStartAutomaticUpdateOnLaunchAsync') -or
    -not $mainWindowText.Contains('_automaticUpdateFailureVersions') -or
    -not $mainWindowText.Contains('RestoreAfterUpdaterExitAsync') -or
    -not $appStartupText.Contains('_services.UpdateService.StartAutomaticChecks()') -or
    -not $mainWindowText.Contains('Monitoring is paused until the verified update is installed')) {
    throw 'The updater must enforce release identity, explicit support policy, SHA-256, pinned Authenticode trust, automatic launch, loop protection and required-update monitoring suspension.'
}

$ciWorkflowText = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\ci.yml') -Raw
if (-not $ciWorkflowText.Contains('0.4.3.${{ github.run_number }}') -or
    -not $ciWorkflowText.Contains('APPLICATION_VERSION: "0.5.0"') -or
    -not $ciWorkflowText.Contains('AchievementRelay-v0.5.0-r${{ github.run_number }}-windows-test') -or
    -not $ciWorkflowText.Contains('--export-collector-card-preview') -or
    -not $ciWorkflowText.Contains('artifacts/AchievementRelay_CollectorCard_Preview.png') -or
    -not $ciWorkflowText.Contains('Start-Process') -or
    -not $ciWorkflowText.Contains('$previewProcess.ExitCode') -or
    -not $ciWorkflowText.Contains('$previewWidth -ne 1200') -or
    -not $ciWorkflowText.Contains('$previewHeight -ne 675') -or
    -not $ciWorkflowText.Contains('tests\AchievementRelay.App.Tests') -or
    -not $ciWorkflowText.Contains('-ApplicationVersion $env:APPLICATION_VERSION') -or
    -not $ciWorkflowText.Contains('artifacts/AchievementRelay_Update.json') -or
    -not $ciWorkflowText.Contains('artifacts/AchievementRelay_Update.sig') -or
    -not $ciWorkflowText.Contains('AchievementRelay.SteamBridge.exe --self-test') -or
    -not $ciWorkflowText.Contains('"protocolVersion":1')) {
    throw 'Pull-request installers must use a monotonically increasing MSIX test revision.'
}

$liveUpdateWorkflowText = Get-Content -LiteralPath (Join-Path $repositoryRoot '.github\workflows\live-update-test.yml') -Raw
if (-not $liveUpdateWorkflowText.Contains('tests\AchievementRelay.Core.Tests') -or
    -not $liveUpdateWorkflowText.Contains('tests\AchievementRelay.App.Tests')) {
    throw 'Controlled live-update validation must run both Core contracts and Collector Card renderer smoke checks.'
}

Write-Host 'Repository structure and package manifest checks passed.' -ForegroundColor Green
