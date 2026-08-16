using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;
using Button = System.Windows.Controls.Button;
using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;
using TextBox = System.Windows.Controls.TextBox;

namespace AchievementRelay.App;

public partial class MainWindow : Window
{
    private const string GitHubUrl = "https://github.com/Conroy1988/Achievement-Relay";
    private const string KoFiUrl = "https://ko-fi.com/D4P124RWI9";
    private const string ArtLicensesUrl = GitHubUrl + "/blob/main/THIRD-PARTY-NOTICES.md";
    private const string OpenXblProfileUrl = "https://xbl.io/profile";
    private const string DiscordWebhookGuideUrl = "https://support.discord.com/hc/en-us/articles/228383668-Intro-to-Webhooks";

    private readonly AppServices _services;
    private readonly ObservableCollection<ActivityEntry> _activity = [];
    private readonly SemaphoreSlim _updatePolicyGate = new(1, 1);
    private AppSettings _settings;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isExiting;
    private bool _hasShownTrayHint;
    private string? _shownUpdateNoticeVersion;
    private bool _enforcingRequiredUpdate;
    private bool _updateOperationInProgress;
    private bool _updateInstallerStarted;
    private bool _requiredPolicyApplied;

    public MainWindow(AppServices services, AppSettings settings)
    {
        InitializeComponent();
        _services = services;
        _settings = settings;

        DashboardActivityList.ItemsSource = _activity;
        ActivityList.ItemsSource = _activity;
        _services.ActivityLog.EntryAdded += OnActivityEntryAdded;
        _services.RelayCoordinator.StatusChanged += OnRelayStatusChanged;
        _services.SteamMonitorCoordinator.StatusChanged += OnSteamStatusChanged;
        _services.UpdateService.StateChanged += OnUpdateStateChanged;

        PopulateControls();
        InitializeTrayIcon();
        ApplyUpdateState(_services.UpdateService.Snapshot);
        RefreshStatus();
    }

    public void ShowSetup()
    {
        MainTabs.SelectedIndex = 1;
        ShowFromTray();
    }

    public void ShowRequiredUpdate()
    {
        MainTabs.SelectedIndex = 0;
        ShowFromTray();
        ApplyUpdateState(_services.UpdateService.Snapshot);
    }

    public void RefreshStatus()
    {
        var apiKeyConfigured = TryGetOpenXblApiKey(out _);
        var accountConfigured = apiKeyConfigured && !string.IsNullOrWhiteSpace(_settings.XboxUserId);
        var webhookConfigured = TryGetWebhook(out _);
        var xboxRunning = _services.RelayCoordinator.IsRunning;
        var steamMonitorRunning = _services.SteamMonitorCoordinator.IsRunning;
        var steamSupported = _services.SteamMonitorCoordinator.IsSupportedPlatform;
        var steamInstalled = _services.SteamMonitorCoordinator.IsSteamInstalled;
        var steamClientRunning = _services.SteamMonitorCoordinator.IsSteamRunning;
        var steamGame = _services.SteamMonitorCoordinator.CurrentGameName;
        var steamPhase = _services.SteamMonitorCoordinator.Phase;
        var steamError = _services.SteamMonitorCoordinator.LastError;
        var relayRunning = xboxRunning || steamMonitorRunning;
        var lastError = _services.RelayCoordinator.LastSyncError;
        var lastSync = _services.RelayCoordinator.LastSuccessfulSync;

        if (accountConfigured)
        {
            var accountLabel = string.IsNullOrWhiteSpace(_settings.XboxGamertag)
                ? "Xbox account connected"
                : $"Connected as {_settings.XboxGamertag}";
            SetStatus(XboxStatusText, "Connected", StatusTone.Success);
            XboxStatusDetail.Text = accountLabel;
            SetupXboxStatus.Text = $"✓ API key stored securely  •  {accountLabel}";
            SetupXboxStatus.Foreground = Brush("AccentBrush");
            SettingsXboxStatus.Text = $"{accountLabel}. The API key is encrypted for this Windows account.";
        }
        else if (apiKeyConfigured)
        {
            SetStatus(XboxStatusText, "Reconnect", StatusTone.Warning);
            XboxStatusDetail.Text = "The saved key needs to be verified";
            SetupXboxStatus.Text = "✓ API key stored securely  •  Select Save and connect to retry verification";
            SetupXboxStatus.Foreground = Brush("WarningBrush");
            SettingsXboxStatus.Text = "An API key is stored, but no Xbox account has been verified.";
        }
        else
        {
            SetStatus(XboxStatusText, "Not connected", StatusTone.Warning);
            XboxStatusDetail.Text = "Complete step 1 in Guided setup";
            SetupXboxStatus.Text = "Status: not connected";
            SetupXboxStatus.Foreground = Brush("WarningBrush");
            SettingsXboxStatus.Text = "No OpenXBL API key or Xbox account is configured.";
        }

        if (!_settings.SteamEnabled)
        {
            SetStatus(SteamStatusText, "Disabled", StatusTone.Neutral);
            SteamStatusDetail.Text = "Enable Steam in Guided setup or Settings";
            SetupSteamStatus.Text = "Status: Steam monitoring is disabled";
            SetupSteamStatus.Foreground = Brush("MutedTextBrush");
            SettingsSteamStatus.Text = "Steam monitoring is disabled.";
        }
        else if (!steamSupported)
        {
            SetStatus(SteamStatusText, "Unavailable", StatusTone.Warning);
            SteamStatusDetail.Text = "Steam on Arm64 requires Windows 11";
            SetupSteamStatus.Text = "Status: Steam monitoring on Arm64 requires Windows 11; Xbox remains available";
            SetupSteamStatus.Foreground = Brush("WarningBrush");
            SettingsSteamStatus.Text = "This Arm64 version of Windows cannot run Steam's x64 achievement component. Upgrade to Windows 11 or use Xbox monitoring.";
        }
        else if (!steamInstalled)
        {
            SetStatus(SteamStatusText, "Steam not found", StatusTone.Warning);
            SteamStatusDetail.Text = "Install or start the desktop Steam client";
            SetupSteamStatus.Text = "Status: Steam is not installed for this Windows user yet";
            SetupSteamStatus.Foreground = Brush("WarningBrush");
            SettingsSteamStatus.Text = "Steam is enabled, but its local installation was not found. Achievement Relay will keep checking.";
        }
        else if (!string.IsNullOrWhiteSpace(steamError))
        {
            SetStatus(SteamStatusText, "Retrying", StatusTone.Warning);
            SteamStatusDetail.Text = steamGame ?? "Steam monitoring will retry automatically";
            SetupSteamStatus.Text = $"Status: {steamError}";
            SetupSteamStatus.Foreground = Brush("WarningBrush");
            SettingsSteamStatus.Text = steamError;
        }
        else if (!string.IsNullOrWhiteSpace(steamGame) && steamPhase == SteamMonitoringPhase.Monitoring)
        {
            SetStatus(SteamStatusText, "Monitoring", StatusTone.Success);
            SteamStatusDetail.Text = steamGame;
            SetupSteamStatus.Text = $"✓ Monitoring {steamGame}; existing unlocks are baselined silently";
            SetupSteamStatus.Foreground = Brush("AccentBrush");
            SettingsSteamStatus.Text = $"Monitoring {steamGame}. Only directly proven live unlocks are relayed.";
        }
        else if (!string.IsNullOrWhiteSpace(steamGame))
        {
            SetStatus(SteamStatusText, "Preparing", StatusTone.Warning);
            SteamStatusDetail.Text = steamPhase switch
            {
                SteamMonitoringPhase.LoadingStats => $"Loading achievement stats for {steamGame}",
                SteamMonitoringPhase.EstablishingBaseline => $"Establishing the safe baseline for {steamGame}",
                _ => $"Connecting the Steam observer for {steamGame}"
            };
            SetupSteamStatus.Text = $"Status: {FormatSteamPhase(steamPhase)} for {steamGame}; wait for baseline confirmation before testing an unlock";
            SetupSteamStatus.Foreground = Brush("WarningBrush");
            SettingsSteamStatus.Text = $"{steamGame} is detected, but achievement monitoring is not ready until the first complete baseline is stored.";
        }
        else if (steamMonitorRunning)
        {
            SetStatus(SteamStatusText, "Ready", StatusTone.Success);
            SteamStatusDetail.Text = steamClientRunning ? "Waiting for a Steam game" : "Waiting for Steam to start";
            SetupSteamStatus.Text = steamClientRunning
                ? "✓ Steam found — waiting for a game to start"
                : "✓ Steam found — monitoring starts automatically with your next game";
            SetupSteamStatus.Foreground = Brush("AccentBrush");
            SettingsSteamStatus.Text = steamClientRunning
                ? "Steam monitoring is ready and waiting for a game."
                : "Steam monitoring is ready and waiting for the Steam client.";
        }
        else
        {
            SetStatus(SteamStatusText, "Ready", StatusTone.Warning);
            SteamStatusDetail.Text = "Finish setup to start monitoring";
            SetupSteamStatus.Text = "✓ Steam found — finish setup to begin monitoring";
            SetupSteamStatus.Foreground = Brush("AccentBrush");
            SettingsSteamStatus.Text = "Steam is installed. Save settings to start automatic monitoring.";
        }

        if (webhookConfigured)
        {
            SetStatus(DiscordStatusText, "Connected", StatusTone.Success);
            DiscordStatusDetail.Text = "Webhook stored with Windows encryption";
            SetupWebhookStatus.Text = "✓ Webhook stored securely  •  Select Save and test to retest it";
            SetupWebhookStatus.Foreground = Brush("AccentBrush");
            SettingsWebhookStatus.Text = "A Discord webhook is configured and encrypted for this Windows account.";
        }
        else
        {
            SetStatus(DiscordStatusText, "Not connected", StatusTone.Warning);
            DiscordStatusDetail.Text = "Complete step 3 in Guided setup";
            SetupWebhookStatus.Text = "Status: not connected";
            SetupWebhookStatus.Foreground = Brush("WarningBrush");
            SettingsWebhookStatus.Text = "No usable Discord webhook is configured.";
        }

        var activeError = (xboxRunning && !string.IsNullOrWhiteSpace(lastError)) ||
                          (steamMonitorRunning && !string.IsNullOrWhiteSpace(steamError));
        var relayLabel = relayRunning
            ? activeError ? "Retrying" : "Monitoring"
            : "Stopped";
        SetStatus(
            RelayStatusText,
            relayLabel,
            relayRunning && !activeError ? StatusTone.Success : StatusTone.Warning);

        var activeProviders = new[]
        {
            xboxRunning ? "Xbox" : null,
            steamMonitorRunning ? "Steam" : null
        }.Where(value => value is not null).Select(value => value!).ToArray();
        RelayStatusDetail.Text = activeProviders.Length == 0
            ? "No achievement source is active"
            : $"{string.Join(" + ", activeProviders)} monitoring";

        SidebarMonitorStatus.Text = relayRunning
            ? activeError ? "● Retrying a provider" : $"● Monitoring {string.Join(" + ", activeProviders)}"
            : "○ Setup required";
        SidebarMonitorStatus.Foreground = Brush(
            relayRunning && !activeError ? "AccentBrush" : "WarningBrush");

        var accountDiagnostic = accountConfigured
            ? string.IsNullOrWhiteSpace(_settings.XboxGamertag) ? "connected" : $"connected as {_settings.XboxGamertag}"
            : "not connected";
        var lastSyncDiagnostic = lastSync is null
            ? "not yet"
            : lastSync.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");
        DiagnosticsStatusText.Text = string.Join(
            Environment.NewLine,
            $"Xbox account: {accountDiagnostic}",
            $"OpenXBL key: {(apiKeyConfigured ? "encrypted and stored" : "not configured")}",
            $"Xbox monitor: {(xboxRunning ? "running" : "not running")}",
            $"Last Xbox sync: {lastSyncDiagnostic}",
            $"Last Xbox error: {(string.IsNullOrWhiteSpace(lastError) ? "none" : lastError)}",
            $"Steam monitoring: {(_settings.SteamEnabled ? steamMonitorRunning ? "running" : "enabled but stopped" : "disabled")}",
            $"Steam platform support: {(steamSupported ? "supported" : "requires Windows 11 on Arm64")}",
            $"Steam installation: {(steamInstalled ? "found" : "not found")}",
            $"Steam client: {(steamClientRunning ? "running" : "not running")}",
            $"Steam game: {steamGame ?? "none detected"}",
            $"Steam phase: {FormatSteamPhase(steamPhase)}",
            $"Last Steam observation: {FormatLocalTimestamp(_services.SteamMonitorCoordinator.LastObservationUtc)}",
            $"Last Steam error: {(string.IsNullOrWhiteSpace(steamError) ? "none" : steamError)}",
            $"Discord: {(webhookConfigured ? "configured" : "not configured")}",
            $"Update status: {FormatUpdateStatus(_services.UpdateService.Snapshot)}",
            $"Windows package version: {_services.UpdateService.CurrentPackageVersion}",
            $"Polling interval: {Math.Clamp(_settings.PollIntervalSeconds, 60, 3600)} seconds",
            $"Install context: {(StartupService.IsPackaged() ? "MSIX packaged" : "classic Windows app")}",
            $"Installer handoff: {_services.Paths.PendingInstallerSetupFile} (deleted after durable import)",
            $"Local data: {_services.Paths.DataDirectory}");

        if (_services.UpdateService.IsUpdateRequired)
        {
            SetStatus(RelayStatusText, "Update required", StatusTone.Error);
            RelayStatusDetail.Text = "Monitoring is paused until the verified update is installed";
            SidebarMonitorStatus.Text = "● Update required";
            SidebarMonitorStatus.Foreground = Brush("ErrorBrush");
        }

        ApplyUpdateState(_services.UpdateService.Snapshot);
    }

    public void PrepareForExit()
    {
        _isExiting = true;
        _services.ActivityLog.EntryAdded -= OnActivityEntryAdded;
        _services.RelayCoordinator.StatusChanged -= OnRelayStatusChanged;
        _services.SteamMonitorCoordinator.StatusChanged -= OnSteamStatusChanged;
        _services.UpdateService.StateChanged -= OnUpdateStateChanged;
        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e) => RefreshStatus();

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        if (!_hasShownTrayHint && _trayIcon is not null)
        {
            _trayIcon.ShowBalloonTip(
                2500,
                "Achievement Relay is still running",
                "Xbox and Steam monitoring continue in the notification area. Use the tray icon to reopen or exit.",
                Forms.ToolTipIcon.Info);
            _hasShownTrayHint = true;
        }
    }

    private void InitializeTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Achievement Relay", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() =>
            (System.Windows.Application.Current as App)?.ExitApplication()));

        var executablePath = Environment.ProcessPath;
        System.Drawing.Icon? extractedIcon = null;
        if (executablePath is { Length: > 0 } && File.Exists(executablePath))
        {
            extractedIcon = System.Drawing.Icon.ExtractAssociatedIcon(executablePath);
        }

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = extractedIcon ?? System.Drawing.SystemIcons.Application,
            Text = "Achievement Relay",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void PopulateControls()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.3.0";
        AboutVersionText.Text = $"Version {version} beta";

        SetupDisplayNameTextBox.Text = _settings.DisplayName;
        SetupSteamEnabledCheckBox.IsChecked = _settings.SteamEnabled;
        SetupStartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        SetupStartMinimizedCheckBox.IsChecked = _settings.StartMinimized;

        SettingsDisplayNameTextBox.Text = _settings.DisplayName;
        SettingsDiscordUsernameTextBox.Text = _settings.DiscordUsername;
        SettingsSteamEnabledCheckBox.IsChecked = _settings.SteamEnabled;
        SettingsRareOnlyCheckBox.IsChecked = _settings.PostRareOnly;
        SettingsRawDetailsCheckBox.IsChecked = _settings.IncludeRawDetailsWhenUncertain;
        SettingsStartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        SettingsStartMinimizedCheckBox.IsChecked = _settings.StartMinimized;

        PopulateSecretControls();
    }

    private void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void ShowDashboard_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 0;

    private void ShowSetup_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 1;

    private void ShowActivity_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 2;

    private void ShowSettings_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 3;

    private void ShowDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 4;
        RefreshStatus();
    }

    private void ShowAbout_Click(object sender, RoutedEventArgs e) => MainTabs.SelectedIndex = 5;

    private void OpenOpenXbl_Click(object sender, RoutedEventArgs e) => OpenExternal(OpenXblProfileUrl);

    private void OpenDiscordWebhookGuide_Click(object sender, RoutedEventArgs e) =>
        OpenExternal(DiscordWebhookGuideUrl);

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        SetButtonBusy(sender, true);
        try
        {
            var result = await _services.UpdateService.CheckAsync(force: true);
            ApplyUpdateState(result);
            if (result.Stage == AppUpdateStage.Current)
            {
                ShowMessage("Achievement Relay is up to date.", MessageBoxImage.Information);
            }
            else if (result.Stage == AppUpdateStage.Failed)
            {
                ShowMessage(result.Message, MessageBoxImage.Warning);
            }
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void UpdateNow_Click(object sender, RoutedEventArgs e)
    {
        if (_updateOperationInProgress || _updateInstallerStarted)
        {
            return;
        }

        _updateOperationInProgress = true;
        SetUpdateActionButtonsEnabled(false);
        try
        {
            var downloaded = await _services.UpdateService.DownloadAsync();
            if (downloaded.Stage != AppUpdateStage.ReadyToInstall)
            {
                ShowMessage(downloaded.Message, MessageBoxImage.Warning);
                return;
            }

            var launch = await _services.UpdateService.LaunchInstallerAsync();
            if (!launch.Success)
            {
                ShowMessage(
                    $"The verified update could not be opened.{Environment.NewLine}{Environment.NewLine}{launch.Message}",
                    MessageBoxImage.Warning);
                return;
            }

            _updateInstallerStarted = true;
            ApplyUpdateState(_services.UpdateService.Snapshot);
            if (launch.ProcessId is int processId)
            {
                _ = MonitorUpdaterExitAsync(processId);
            }
        }
        finally
        {
            _updateOperationInProgress = false;
            if (!_updateInstallerStarted)
            {
                SetUpdateActionButtonsEnabled(true);
            }
        }
    }

    private void OpenUpdateNotes_Click(object sender, RoutedEventArgs e)
    {
        if (_services.UpdateService.Snapshot.ReleasePage is Uri releasePage)
        {
            OpenExternal(releasePage.ToString());
        }
    }

    private void ToggleSetupXboxApiKeyVisibility_Click(object sender, RoutedEventArgs e) =>
        ToggleSecretVisibility(
            SetupXboxApiKeyPasswordBox,
            SetupXboxApiKeyRevealTextBox,
            SetupXboxApiKeyRevealButton,
            "Reveal Key",
            "Hide Key");

    private void ToggleSettingsXboxApiKeyVisibility_Click(object sender, RoutedEventArgs e) =>
        ToggleSecretVisibility(
            SettingsXboxApiKeyPasswordBox,
            SettingsXboxApiKeyRevealTextBox,
            SettingsXboxApiKeyRevealButton,
            "Reveal Key",
            "Hide Key");

    private void ToggleSetupWebhookVisibility_Click(object sender, RoutedEventArgs e) =>
        ToggleSecretVisibility(
            SetupWebhookPasswordBox,
            SetupWebhookRevealTextBox,
            SetupWebhookRevealButton,
            "Reveal Webhook",
            "Hide Webhook");

    private void ToggleSettingsWebhookVisibility_Click(object sender, RoutedEventArgs e) =>
        ToggleSecretVisibility(
            SettingsWebhookPasswordBox,
            SettingsWebhookRevealTextBox,
            SettingsWebhookRevealButton,
            "Reveal Webhook",
            "Hide Webhook");

    private async void SaveAndTestOpenXbl_Click(object sender, RoutedEventArgs e) =>
        await SaveAndTestOpenXblAsync(
            sender,
            GetSecretValue(SetupXboxApiKeyPasswordBox, SetupXboxApiKeyRevealTextBox),
            SetupXboxStatus);

    private async void SaveAndTestSettingsOpenXbl_Click(object sender, RoutedEventArgs e) =>
        await SaveAndTestOpenXblAsync(
            sender,
            GetSecretValue(SettingsXboxApiKeyPasswordBox, SettingsXboxApiKeyRevealTextBox),
            SettingsXboxStatus);

    private async Task SaveAndTestOpenXblAsync(object sender, string value, TextBlock statusTarget)
    {
        var candidate = value;
        if (string.IsNullOrWhiteSpace(candidate) && TryGetOpenXblApiKey(out var storedApiKey))
        {
            candidate = storedApiKey;
        }

        if (!OpenXblApiKeyValidator.TryNormalize(candidate, out var apiKey, out var error))
        {
            statusTarget.Text = $"Status: {error}";
            statusTarget.Foreground = Brush("ErrorBrush");
            return;
        }

        SetButtonBusy(sender, true);
        statusTarget.Text = "Status: checking the Xbox account and achievement feed…";
        statusTarget.Foreground = Brush("WarningBrush");
        try
        {
            _settings = _settings with
            {
                ProtectedOpenXblApiKey = _services.WebhookProtector.ProtectOpenXblApiKey(apiKey)
            };
            await _services.SettingsStore.SaveAsync(_settings);
            PopulateSecretControls();

            var accountResult = await _services.OpenXblClient.GetAccountAsync(apiKey);
            if (!accountResult.Success || accountResult.Account is null)
            {
                RefreshStatus();
                statusTarget.Text = $"Status: API key saved, but {accountResult.Message}";
                statusTarget.Foreground = Brush("ErrorBrush");
                _services.ActivityLog.Warning(accountResult.Message);
                return;
            }

            var titleProgressResult = await _services.OpenXblClient.GetTitleProgressAsync(
                apiKey,
                accountResult.Account.Xuid);
            if (!titleProgressResult.Success || titleProgressResult.Titles is null)
            {
                RefreshStatus();
                statusTarget.Text = $"Status: account found, but {titleProgressResult.Message}";
                statusTarget.Foreground = Brush("ErrorBrush");
                _services.ActivityLog.Warning(titleProgressResult.Message);
                return;
            }

            await _services.RelayCoordinator.StopAsync();
            var previousAccount = _settings.XboxUserId;
            var accountChanged = !string.Equals(
                previousAccount,
                accountResult.Account.Xuid,
                StringComparison.Ordinal);
            var displayName = string.IsNullOrWhiteSpace(_settings.DisplayName)
                ? accountResult.Account.Gamertag
                : _settings.DisplayName;
            _settings = _settings with
            {
                ProtectedOpenXblApiKey = _services.WebhookProtector.ProtectOpenXblApiKey(apiKey),
                XboxUserId = accountResult.Account.Xuid,
                XboxGamertag = accountResult.Account.Gamertag,
                DisplayName = displayName
            };
            await _services.SettingsStore.SaveAsync(_settings);

            var state = await _services.SyncStateStore.LoadAsync();
            var needsBaseline = accountChanged ||
                                state.BaselineUtc is null ||
                                !string.Equals(state.AccountXuid, accountResult.Account.Xuid, StringComparison.Ordinal);
            if (needsBaseline)
            {
                await _services.SyncStateStore.ResetAsync(
                    accountResult.Account.Xuid,
                    DateTimeOffset.UtcNow,
                    titleProgressResult.Titles);
            }

            PopulateControls();
            RefreshStatus();

            var baselineNote = needsBaseline
                ? " Earlier unlocks were baselined and will not be posted."
                : " The existing sync position was preserved.";
            statusTarget.Text = $"Status: connected as {accountResult.Account.Gamertag}.{baselineNote}";
            statusTarget.Foreground = Brush("AccentBrush");
            _services.ActivityLog.Success("OpenXBL account connection verified. Existing achievements will not be reposted.");

            if (_settings.SetupCompleted &&
                !_services.UpdateService.IsUpdateRequired &&
                TryGetWebhook(out _))
            {
                await _services.RelayCoordinator.StartAsync();
                RefreshStatus();
            }
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void SaveAndTestWebhook_Click(object sender, RoutedEventArgs e)
    {
        var value = GetSecretValue(SetupWebhookPasswordBox, SetupWebhookRevealTextBox);
        Uri? webhookUri;
        string? error = null;
        if (string.IsNullOrWhiteSpace(value) &&
            TryGetWebhook(out var storedWebhook) &&
            storedWebhook is not null)
        {
            webhookUri = storedWebhook;
        }
        else if (!WebhookUrlValidator.TryNormalize(value, out webhookUri, out error) || webhookUri is null)
        {
            SetupWebhookStatus.Text = $"Status: {error}";
            SetupWebhookStatus.Foreground = Brush("ErrorBrush");
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            _settings = _settings with { ProtectedWebhookUrl = _services.WebhookProtector.Protect(webhookUri.ToString()) };
            await _services.SettingsStore.SaveAsync(_settings);
            PopulateSecretControls();

            var result = await _services.WebhookClient.SendAsync(
                webhookUri,
                DiscordWebhookPayloadFactory.CreateConnectionTest(_settings));

            RefreshStatus();
            SetupWebhookStatus.Text = result.Success
                ? "Status: connected — check Discord for the test post"
                : $"Status: saved, but the test failed — {result.Message}";
            SetupWebhookStatus.Foreground = Brush(result.Success ? "AccentBrush" : "ErrorBrush");
            _services.ActivityLog.Info(result.Success
                ? "Discord webhook saved and tested successfully."
                : $"Discord webhook test failed: {result.Message}");
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void FinishSetup_Click(object sender, RoutedEventArgs e)
    {
        if (BlockForRequiredUpdate())
        {
            return;
        }

        var xboxConfigured = TryGetOpenXblApiKey(out _) && !string.IsNullOrWhiteSpace(_settings.XboxUserId);
        var steamEnabled = SetupSteamEnabledCheckBox.IsChecked == true;
        if (!xboxConfigured && !steamEnabled)
        {
            ShowMessage("Choose at least one source: connect Xbox through OpenXBL, enable Steam monitoring, or use both.", MessageBoxImage.Warning);
            return;
        }

        if (!TryGetWebhook(out _))
        {
            ShowMessage("Complete step 3 and save a valid Discord webhook before finishing setup.", MessageBoxImage.Warning);
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            var startWithWindows = SetupStartWithWindowsCheckBox.IsChecked == true;
            _settings = _settings with
            {
                DisplayName = NormalizePlayerName(SetupDisplayNameTextBox.Text),
                SteamEnabled = steamEnabled,
                StartWithWindows = startWithWindows,
                StartMinimized = SetupStartMinimizedCheckBox.IsChecked == true,
                SetupCompleted = true
            };

            await _services.SettingsStore.SaveAsync(_settings);
            var startupApplied = await _services.StartupService.SetEnabledAsync(startWithWindows);
            if (!xboxConfigured)
            {
                await _services.RelayCoordinator.StopAsync();
            }

            if (!steamEnabled)
            {
                await _services.SteamMonitorCoordinator.StopAsync();
            }

            var xboxStarted = xboxConfigured && await _services.RelayCoordinator.StartAsync();
            var steamStarted = steamEnabled && await _services.SteamMonitorCoordinator.StartAsync();
            if (!xboxStarted && !steamStarted)
            {
                _settings = _settings with { SetupCompleted = false };
                await _services.SettingsStore.SaveAsync(_settings);
                PopulateControls();
                RefreshStatus();
                var providerError = steamEnabled
                    ? _services.SteamMonitorCoordinator.LastError
                    : _services.RelayCoordinator.LastSyncError;
                ShowMessage(
                    providerError ?? "Achievement monitoring could not start. Check Diagnostics, then reinstall if a required component is reported missing.",
                    MessageBoxImage.Error);
                return;
            }

            PopulateControls();
            RefreshStatus();
            MainTabs.SelectedIndex = 0;
            var sources = xboxStarted && steamStarted ? "Xbox and Steam" : xboxStarted ? "Xbox" : "Steam";
            _services.ActivityLog.Success($"Guided setup completed. {sources} monitoring is active.");

            var startupNote = startWithWindows && !startupApplied
                ? Environment.NewLine + Environment.NewLine + "Windows did not enable startup automatically. You can enable Achievement Relay in Settings > Apps > Startup."
                : string.Empty;
            ShowMessage(
                $"Setup is complete. Achievement Relay is monitoring {sources}. Existing achievements are a silent baseline; only later unlocks are posted." + startupNote,
                MessageBoxImage.Information);
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void SendSampleAchievement_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetWebhook(out var webhookUri) || webhookUri is null)
        {
            ShowMessage("Connect a Discord webhook in Guided setup first.", MessageBoxImage.Warning);
            MainTabs.SelectedIndex = 1;
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            var sample = new AchievementEvent
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = "Relay online",
                Description = "This is a sample achievement from Achievement Relay.",
                GameName = "Achievement Relay Setup",
                Gamerscore = 10,
                IsRare = true,
                SourceProvider = "Achievement Relay",
                UnlockedAt = DateTimeOffset.UtcNow
            };
            var result = await _services.WebhookClient.SendAsync(
                webhookUri,
                DiscordWebhookPayloadFactory.Create(sample, _settings));
            _services.ActivityLog.Info(result.Success
                ? "Sample achievement posted to Discord."
                : $"Sample achievement failed: {result.Message}");
            ShowMessage(result.Message, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void TestSettingsWebhook_Click(object sender, RoutedEventArgs e)
    {
        Uri? webhookUri;
        var settingsForTest = _settings with
        {
            DiscordUsername = NormalizeWebhookName(SettingsDiscordUsernameTextBox.Text)
        };
        var replacement = GetSecretValue(SettingsWebhookPasswordBox, SettingsWebhookRevealTextBox);
        if (!string.IsNullOrWhiteSpace(replacement))
        {
            if (!WebhookUrlValidator.TryNormalize(replacement, out webhookUri, out var error) || webhookUri is null)
            {
                ShowMessage(error ?? "The webhook URL is invalid.", MessageBoxImage.Warning);
                return;
            }
        }
        else if (!TryGetWebhook(out webhookUri) || webhookUri is null)
        {
            ShowMessage("Paste a Discord webhook URL first.", MessageBoxImage.Warning);
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            var result = await _services.WebhookClient.SendAsync(
                webhookUri,
                DiscordWebhookPayloadFactory.CreateConnectionTest(settingsForTest));
            ShowMessage(result.Message, result.Success ? MessageBoxImage.Information : MessageBoxImage.Error);
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var protectedWebhook = _settings.ProtectedWebhookUrl;
        var replacement = GetSecretValue(SettingsWebhookPasswordBox, SettingsWebhookRevealTextBox);
        if (!string.IsNullOrWhiteSpace(replacement))
        {
            if (!WebhookUrlValidator.TryNormalize(replacement, out var webhookUri, out var error) || webhookUri is null)
            {
                ShowMessage(error ?? "The webhook URL is invalid.", MessageBoxImage.Warning);
                return;
            }

            protectedWebhook = _services.WebhookProtector.Protect(webhookUri.ToString());
        }

        SetButtonBusy(sender, true);
        try
        {
            var startWithWindows = SettingsStartWithWindowsCheckBox.IsChecked == true;
            _settings = _settings with
            {
                ProtectedWebhookUrl = protectedWebhook,
                DiscordUsername = NormalizeWebhookName(SettingsDiscordUsernameTextBox.Text),
                DisplayName = NormalizePlayerName(SettingsDisplayNameTextBox.Text),
                SteamEnabled = SettingsSteamEnabledCheckBox.IsChecked == true,
                PostRareOnly = SettingsRareOnlyCheckBox.IsChecked == true,
                IncludeRawDetailsWhenUncertain = SettingsRawDetailsCheckBox.IsChecked == true,
                StartWithWindows = startWithWindows,
                StartMinimized = SettingsStartMinimizedCheckBox.IsChecked == true
            };

            var xboxConfigured = TryGetOpenXblApiKey(out _) && !string.IsNullOrWhiteSpace(_settings.XboxUserId);
            var webhookConfigured = TryGetWebhook(out _) || !string.IsNullOrWhiteSpace(replacement);
            var hasProvider = xboxConfigured || _settings.SteamEnabled;
            _settings = _settings with { SetupCompleted = _settings.SetupCompleted && webhookConfigured && hasProvider };
            await _services.SettingsStore.SaveAsync(_settings);
            var startupApplied = await _services.StartupService.SetEnabledAsync(startWithWindows);
            if (_services.UpdateService.IsUpdateRequired)
            {
                await _services.RelayCoordinator.StopAsync();
                await _services.SteamMonitorCoordinator.StopAsync();
            }
            else if (!xboxConfigured)
            {
                await _services.RelayCoordinator.StopAsync();
            }
            else if (_settings.SetupCompleted && webhookConfigured)
            {
                await _services.RelayCoordinator.StartAsync();
            }

            if (!_services.UpdateService.IsUpdateRequired && !_settings.SteamEnabled)
            {
                await _services.SteamMonitorCoordinator.StopAsync();
            }
            else if (!_services.UpdateService.IsUpdateRequired &&
                     _settings.SetupCompleted &&
                     webhookConfigured)
            {
                // Preference/webhook edits are read at delivery time. Keep an
                // active helper session intact so saving unrelated settings
                // cannot create a small observation gap during gameplay.
                await _services.SteamMonitorCoordinator.StartAsync();
            }

            PopulateControls();

            RefreshStatus();
            _services.ActivityLog.Success("Settings saved.");

            var message = startWithWindows && !startupApplied
                ? "Settings were saved, but Windows did not enable automatic startup. Enable Achievement Relay in Windows Startup Apps."
                : "Settings saved.";
            ShowMessage(message, startWithWindows && !startupApplied ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void RemoveOpenXbl_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            this,
            "Disconnect the saved Xbox account and remove its OpenXBL API key from this PC? The Discord webhook will be kept.",
            "Achievement Relay",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            await _services.RelayCoordinator.StopAsync();
            var keepSetupCompleted = _settings.SetupCompleted && _settings.SteamEnabled && TryGetWebhook(out _);
            _settings = _settings with
            {
                ProtectedOpenXblApiKey = string.Empty,
                XboxUserId = string.Empty,
                XboxGamertag = string.Empty,
                SetupCompleted = keepSetupCompleted
            };
            await _services.SettingsStore.SaveAsync(_settings);
            await _services.SyncStateStore.ClearAsync();
            _services.ActivityLog.Info("Saved OpenXBL connection removed.");
            PopulateControls();
            RefreshStatus();
            if (!keepSetupCompleted)
            {
                MainTabs.SelectedIndex = 1;
            }
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void RemoveWebhook_Click(object sender, RoutedEventArgs e)
    {
        var confirmation = MessageBox.Show(
            this,
            "Remove the saved Discord webhook from this PC? Achievement Relay will stop posting until another webhook is configured.",
            "Achievement Relay",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            await _services.RelayCoordinator.StopAsync();
            await _services.SteamMonitorCoordinator.StopAsync();
            _settings = _settings with { ProtectedWebhookUrl = string.Empty, SetupCompleted = false };
            await _services.SettingsStore.SaveAsync(_settings);
            _services.ActivityLog.Info("Saved Discord webhook removed.");
            PopulateControls();
            RefreshStatus();
            MainTabs.SelectedIndex = 1;
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void RefreshSteam_Click(object sender, RoutedEventArgs e)
    {
        if (BlockForRequiredUpdate())
        {
            return;
        }

        var enabled = MainTabs.SelectedIndex == 1
            ? SetupSteamEnabledCheckBox.IsChecked == true
            : SettingsSteamEnabledCheckBox.IsChecked == true;
        if (!enabled)
        {
            await _services.SteamMonitorCoordinator.StopAsync();
            _settings = _settings with { SteamEnabled = false };
            await _services.SettingsStore.SaveAsync(_settings);
            PopulateControls();
            RefreshStatus();
            ShowMessage("Steam monitoring is switched off. Enable it first, then refresh.", MessageBoxImage.Information);
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            _settings = _settings with { SteamEnabled = true };
            await _services.SettingsStore.SaveAsync(_settings);
            var started = await _services.SteamMonitorCoordinator.RestartAsync();
            PopulateControls();
            RefreshStatus();
            if (!started)
            {
                ShowMessage(
                    _services.SteamMonitorCoordinator.LastError ??
                    "Steam monitoring could not start. Reinstall Achievement Relay if Diagnostics reports that the component is missing.",
                    MessageBoxImage.Warning);
                return;
            }

            var game = _services.SteamMonitorCoordinator.CurrentGameName;
            var phase = _services.SteamMonitorCoordinator.Phase;
            ShowMessage(
                game is null
                    ? "Steam monitoring is ready. Start a Steam game normally; Achievement Relay will baseline its existing unlocks silently and then watch for new ones."
                    : phase == SteamMonitoringPhase.Monitoring
                        ? $"Steam monitoring is active for {game}. Only new unlocks will be relayed."
                        : $"{game} is detected, but Steam is still {FormatSteamPhase(phase)}. Wait for Activity to confirm that the baseline is established before testing an unlock.",
                MessageBoxImage.Information);
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        if (BlockForRequiredUpdate())
        {
            return;
        }

        if (!TryGetOpenXblApiKey(out _) || string.IsNullOrWhiteSpace(_settings.XboxUserId))
        {
            ShowMessage("Connect the Xbox account in Guided setup before syncing.", MessageBoxImage.Warning);
            MainTabs.SelectedIndex = 1;
            return;
        }

        if (!TryGetWebhook(out _))
        {
            ShowMessage("Connect the Discord webhook before syncing so new achievements have a destination.", MessageBoxImage.Warning);
            MainTabs.SelectedIndex = 1;
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            if (_settings.SetupCompleted)
            {
                await _services.RelayCoordinator.StartAsync();
            }

            var outcome = await _services.RelayCoordinator.SyncNowAsync();
            RefreshStatus();
            ShowMessage(outcome.Message, outcome.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        OpenExternal(_services.Paths.DataDirectory);

    private void CopySupportSummary_Click(object sender, RoutedEventArgs e)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
        var lastSync = _services.RelayCoordinator.LastSuccessfulSync;
        var summary = string.Join(
            Environment.NewLine,
            "Achievement Relay support summary",
            $"Version: {version}",
            $"Generated: {DateTimeOffset.Now:O}",
            $"Windows: {Environment.OSVersion}",
            $"Packaged: {StartupService.IsPackaged()}",
            $"OpenXBL configured: {TryGetOpenXblApiKey(out _)}",
            $"Xbox account verified: {!string.IsNullOrWhiteSpace(_settings.XboxUserId)}",
            $"Xbox monitor running: {_services.RelayCoordinator.IsRunning}",
            $"Last Xbox sync: {(lastSync is null ? "not yet" : lastSync.Value.ToString("O"))}",
            $"Last Xbox error: {_services.RelayCoordinator.LastSyncError ?? "none"}",
            $"Steam enabled: {_settings.SteamEnabled}",
            $"Steam platform supported: {_services.SteamMonitorCoordinator.IsSupportedPlatform}",
            $"Steam installed: {_services.SteamMonitorCoordinator.IsSteamInstalled}",
            $"Steam client running: {_services.SteamMonitorCoordinator.IsSteamRunning}",
            $"Steam monitor running: {_services.SteamMonitorCoordinator.IsRunning}",
            $"Steam game detected: {_services.SteamMonitorCoordinator.CurrentGameName ?? "none"}",
            $"Steam phase: {FormatSteamPhase(_services.SteamMonitorCoordinator.Phase)}",
            $"Last Steam observation: {(_services.SteamMonitorCoordinator.LastObservationUtc?.ToString("O") ?? "not yet")}",
            $"Last Steam error: {_services.SteamMonitorCoordinator.LastError ?? "none"}",
            $"Discord configured: {TryGetWebhook(out _)}",
            $"Setup completed: {_settings.SetupCompleted}",
            $"Update status: {FormatUpdateStatus(_services.UpdateService.Snapshot)}",
            $"Windows package version: {_services.UpdateService.CurrentPackageVersion}",
            $"Data folder: {_services.Paths.DataDirectory}");

        System.Windows.Clipboard.SetText(summary);
        ShowMessage(
            "Support summary copied. It does not contain the API key, Xbox or Steam account IDs, gamertag, Steam name, webhook URL or token.",
            MessageBoxImage.Information);
    }

    private void OpenPrivacyPolicy_Click(object sender, RoutedEventArgs e)
    {
        var localPrivacyPolicy = Path.Combine(AppContext.BaseDirectory, "PRIVACY.md");
        OpenExternal(File.Exists(localPrivacyPolicy) ? localPrivacyPolicy : $"{GitHubUrl}/blob/main/PRIVACY.md");
    }

    private void OpenArtLicenses_Click(object sender, RoutedEventArgs e)
    {
        var localNotices = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
        OpenExternal(File.Exists(localNotices) ? localNotices : ArtLicensesUrl);
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenExternal(GitHubUrl);

    private void OpenKoFi_Click(object sender, RoutedEventArgs e) => OpenExternal(KoFiUrl);

    private void OnRelayStatusChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(RefreshStatus);

    private void OnSteamStatusChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(RefreshStatus);

    private void OnUpdateStateChanged(object? sender, AppUpdateSnapshot snapshot)
    {
        Dispatcher.InvokeAsync(() =>
        {
            ApplyUpdateState(snapshot);
            _ = ApplyUpdatePolicyAsync(snapshot);
        });
    }

    private void ApplyUpdateState(AppUpdateSnapshot snapshot)
    {
        AboutUpdateStatusText.Text = snapshot.Message;
        AboutUpdateStatusText.Foreground = snapshot.IsRequired
            ? Brush("ErrorBrush")
            : snapshot.Stage == AppUpdateStage.Current
                ? Brush("AccentBrush")
                : snapshot.Stage == AppUpdateStage.Failed
                    ? Brush("WarningBrush")
                    : Brush("MutedTextBrush");
        AboutUpdateCheckedText.Text = snapshot.LastCheckedUtc is DateTimeOffset checkedAt
            ? $"Last checked {checkedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss zzz}"
            : "Automatic checks use the official GitHub Releases feed.";

        var hasReleasePage = snapshot.ReleasePage is not null;
        AboutUpdateNotesButton.Visibility = hasReleasePage ? Visibility.Visible : Visibility.Collapsed;
        UpdateBannerNotesButton.Visibility = hasReleasePage ? Visibility.Visible : Visibility.Collapsed;
        AboutUpdateActionButton.Visibility = snapshot.HasUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateBannerActionButton.Visibility = snapshot.HasUpdate ? Visibility.Visible : Visibility.Collapsed;
        UpdateBanner.Visibility = snapshot.HasUpdate ? Visibility.Visible : Visibility.Collapsed;

        if (snapshot.HasUpdate)
        {
            UpdateBannerTitle.Text = snapshot.IsRequired
                ? $"UPDATE REQUIRED · VERSION {snapshot.LatestVersion}"
                : $"UPDATE AVAILABLE · VERSION {snapshot.LatestVersion}";
            UpdateBannerMessage.Text = snapshot.Message;
            UpdateBanner.BorderBrush = snapshot.IsRequired
                ? Brush("ErrorBrush")
                : Brush("WarningBrush");
        }

        var downloading = snapshot.Stage == AppUpdateStage.Downloading;
        UpdateBannerProgress.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
        UpdateBannerProgress.Value = Math.Clamp(snapshot.DownloadProgress ?? 0, 0, 1);

        var actionLabel = _updateInstallerStarted
            ? "Updater open"
            : snapshot.Stage == AppUpdateStage.ReadyToInstall
                ? "Install update"
                : snapshot.Stage == AppUpdateStage.Downloading
                    ? $"Downloading {snapshot.DownloadProgress ?? 0:P0}"
                    : snapshot.Stage == AppUpdateStage.Failed
                        ? "Retry update"
                        : "Update now";
        UpdateBannerActionButton.Content = actionLabel;
        AboutUpdateActionButton.Content = actionLabel;
        SetUpdateActionButtonsEnabled(
            !downloading && !_updateOperationInProgress && !_updateInstallerStarted);

        if (snapshot.HasUpdate &&
            (snapshot.Stage is AppUpdateStage.Available or AppUpdateStage.Required) &&
            !string.Equals(_shownUpdateNoticeVersion, snapshot.LatestVersion, StringComparison.Ordinal) &&
            _trayIcon is not null)
        {
            _trayIcon.ShowBalloonTip(
                5000,
                snapshot.IsRequired ? "Achievement Relay update required" : "Achievement Relay update available",
                snapshot.IsRequired
                    ? $"Monitoring is paused until version {snapshot.LatestVersion} is installed."
                    : $"Version {snapshot.LatestVersion} can be installed from the app.",
                snapshot.IsRequired ? Forms.ToolTipIcon.Warning : Forms.ToolTipIcon.Info);
            _shownUpdateNoticeVersion = snapshot.LatestVersion;
        }
    }

    private async Task EnforceRequiredUpdateAsync()
    {
        if (_enforcingRequiredUpdate)
        {
            return;
        }

        _enforcingRequiredUpdate = true;
        try
        {
            await _services.RelayCoordinator.StopAsync();
            await _services.SteamMonitorCoordinator.StopAsync();
            RefreshStatus();
            ShowRequiredUpdate();
        }
        finally
        {
            _enforcingRequiredUpdate = false;
        }
    }

    private async Task ApplyUpdatePolicyAsync(AppUpdateSnapshot snapshot)
    {
        await _updatePolicyGate.WaitAsync();
        try
        {
            if (!ReferenceEquals(snapshot, _services.UpdateService.Snapshot))
            {
                return;
            }

            if (snapshot.IsRequired)
            {
                if (!_requiredPolicyApplied)
                {
                    await EnforceRequiredUpdateAsync();
                    _requiredPolicyApplied = true;
                }
            }
            else if (_requiredPolicyApplied)
            {
                await ResumeMonitoringAfterUpdatePolicyAsync();
                _requiredPolicyApplied = false;
            }
        }
        catch (Exception exception)
        {
            _services.ActivityLog.Warning($"Update policy transition failed safely: {exception.Message}");
        }
        finally
        {
            _updatePolicyGate.Release();
        }
    }

    private async Task ResumeMonitoringAfterUpdatePolicyAsync()
    {
        if (!_settings.SetupCompleted || !TryGetWebhook(out _))
        {
            RefreshStatus();
            return;
        }

        var xboxConfigured = TryGetOpenXblApiKey(out _) &&
                             !string.IsNullOrWhiteSpace(_settings.XboxUserId);
        if (xboxConfigured)
        {
            await _services.RelayCoordinator.StartAsync();
        }
        if (_settings.SteamEnabled)
        {
            await _services.SteamMonitorCoordinator.StartAsync();
        }

        RefreshStatus();
    }

    private async Task MonitorUpdaterExitAsync(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            await process.WaitForExitAsync();
        }
        catch (SystemException)
        {
            // The Setup bootstrap process may hand off to its extracted process.
        }
        finally
        {
            await Dispatcher.InvokeAsync(() =>
            {
                _updateInstallerStarted = false;
                ApplyUpdateState(_services.UpdateService.Snapshot);
            });
        }
    }

    private bool BlockForRequiredUpdate()
    {
        if (!_services.UpdateService.IsUpdateRequired)
        {
            return false;
        }

        ShowRequiredUpdate();
        ShowMessage(
            "This release is below the minimum supported version. Install the verified update before restarting achievement monitoring.",
            MessageBoxImage.Warning);
        return true;
    }

    private void SetUpdateActionButtonsEnabled(bool enabled)
    {
        UpdateBannerActionButton.IsEnabled = enabled;
        AboutUpdateActionButton.IsEnabled = enabled;
    }

    private static string FormatUpdateStatus(AppUpdateSnapshot snapshot) => snapshot.Stage switch
    {
        AppUpdateStage.Current => $"current ({snapshot.CurrentVersion})",
        AppUpdateStage.Available => $"version {snapshot.LatestVersion} available",
        AppUpdateStage.Required => $"version {snapshot.LatestVersion} required",
        AppUpdateStage.Downloading => $"downloading version {snapshot.LatestVersion}",
        AppUpdateStage.ReadyToInstall => $"version {snapshot.LatestVersion} ready to install",
        AppUpdateStage.Checking => "checking GitHub",
        AppUpdateStage.Failed => "check or download failed",
        _ => "not checked"
    };

    private void OnActivityEntryAdded(object? sender, ActivityEntry entry)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _activity.Insert(0, entry);
            while (_activity.Count > 300)
            {
                _activity.RemoveAt(_activity.Count - 1);
            }
        });
    }

    private bool TryGetWebhook(out Uri? webhookUri)
    {
        var value = _services.WebhookProtector.TryUnprotect(_settings.ProtectedWebhookUrl);
        return WebhookUrlValidator.TryNormalize(value, out webhookUri, out _);
    }

    private bool TryGetOpenXblApiKey(out string apiKey)
    {
        var value = _services.WebhookProtector.TryUnprotectOpenXblApiKey(_settings.ProtectedOpenXblApiKey);
        return OpenXblApiKeyValidator.TryNormalize(value, out apiKey, out _);
    }

    private void PopulateSecretControls()
    {
        var apiKey = TryGetOpenXblApiKey(out var storedApiKey) ? storedApiKey : string.Empty;
        var webhook = TryGetWebhook(out var storedWebhook) && storedWebhook is not null
            ? storedWebhook.ToString()
            : string.Empty;

        SetSecretValue(
            SetupXboxApiKeyPasswordBox,
            SetupXboxApiKeyRevealTextBox,
            SetupXboxApiKeyRevealButton,
            apiKey,
            "Reveal Key");
        SetSecretValue(
            SettingsXboxApiKeyPasswordBox,
            SettingsXboxApiKeyRevealTextBox,
            SettingsXboxApiKeyRevealButton,
            apiKey,
            "Reveal Key");
        SetSecretValue(
            SetupWebhookPasswordBox,
            SetupWebhookRevealTextBox,
            SetupWebhookRevealButton,
            webhook,
            "Reveal Webhook");
        SetSecretValue(
            SettingsWebhookPasswordBox,
            SettingsWebhookRevealTextBox,
            SettingsWebhookRevealButton,
            webhook,
            "Reveal Webhook");
    }

    private static string GetSecretValue(PasswordBox passwordBox, TextBox revealTextBox) =>
        revealTextBox.Visibility == Visibility.Visible ? revealTextBox.Text : passwordBox.Password;

    private static void SetSecretValue(
        PasswordBox passwordBox,
        TextBox revealTextBox,
        Button revealButton,
        string value,
        string revealLabel)
    {
        revealTextBox.Clear();
        revealTextBox.Visibility = Visibility.Collapsed;
        passwordBox.Password = value;
        passwordBox.Visibility = Visibility.Visible;
        revealButton.Content = revealLabel;
        revealButton.IsEnabled = true;
    }

    private static void ToggleSecretVisibility(
        PasswordBox passwordBox,
        TextBox revealTextBox,
        Button revealButton,
        string revealLabel,
        string hideLabel)
    {
        if (revealTextBox.Visibility == Visibility.Visible)
        {
            passwordBox.Password = revealTextBox.Text;
            revealTextBox.Clear();
            revealTextBox.Visibility = Visibility.Collapsed;
            passwordBox.Visibility = Visibility.Visible;
            revealButton.Content = revealLabel;
            passwordBox.Focus();
            return;
        }

        revealTextBox.Text = passwordBox.Password;
        passwordBox.Visibility = Visibility.Collapsed;
        revealTextBox.Visibility = Visibility.Visible;
        revealButton.Content = hideLabel;
        revealTextBox.Focus();
        revealTextBox.CaretIndex = revealTextBox.Text.Length;
    }

    private string NormalizePlayerName(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (!string.IsNullOrWhiteSpace(_settings.XboxGamertag))
        {
            return _settings.XboxGamertag;
        }

        return _services.SteamMonitorCoordinator.SteamPlayerName ?? string.Empty;
    }

    private static string NormalizeWebhookName(string value) =>
        string.IsNullOrWhiteSpace(value) ? "Achievement Relay" : value.Trim();

    private static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            MessageBox.Show(
                $"Windows could not open that location.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Achievement Relay",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ShowMessage(string message, MessageBoxImage icon) =>
        MessageBox.Show(this, message, "Achievement Relay", MessageBoxButton.OK, icon);

    private static void SetButtonBusy(object sender, bool busy)
    {
        if (sender is Button button)
        {
            button.IsEnabled = !busy;
        }
    }

    private void SetStatus(TextBlock target, string value, StatusTone tone)
    {
        target.Text = value;
        target.Foreground = tone switch
        {
            StatusTone.Success => Brush("AccentBrush"),
            StatusTone.Warning => Brush("WarningBrush"),
            StatusTone.Error => Brush("ErrorBrush"),
            _ => Brush("MutedTextBrush")
        };
    }

    private static string FormatLocalTimestamp(DateTimeOffset? value) =>
        value is null ? "not yet" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static string FormatSteamPhase(SteamMonitoringPhase phase) => phase switch
    {
        SteamMonitoringPhase.WaitingForGame => "waiting for a game",
        SteamMonitoringPhase.Connecting => "connecting",
        SteamMonitoringPhase.LoadingStats => "loading achievement stats",
        SteamMonitoringPhase.EstablishingBaseline => "establishing the baseline",
        SteamMonitoringPhase.Monitoring => "monitoring",
        SteamMonitoringPhase.Retrying => "retrying",
        _ => "unknown"
    };

    private System.Windows.Media.Brush Brush(string resourceName) =>
        (System.Windows.Media.Brush)FindResource(resourceName);

    private enum StatusTone
    {
        Success,
        Warning,
        Error,
        Neutral
    }
}
