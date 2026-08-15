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
    private AppSettings _settings;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isExiting;
    private bool _hasShownTrayHint;

    public MainWindow(AppServices services, AppSettings settings)
    {
        InitializeComponent();
        _services = services;
        _settings = settings;

        DashboardActivityList.ItemsSource = _activity;
        ActivityList.ItemsSource = _activity;
        _services.ActivityLog.EntryAdded += OnActivityEntryAdded;
        _services.RelayCoordinator.StatusChanged += OnRelayStatusChanged;

        PopulateControls();
        InitializeTrayIcon();
        RefreshStatus();
    }

    public void ShowSetup()
    {
        MainTabs.SelectedIndex = 1;
        ShowFromTray();
    }

    public void RefreshStatus()
    {
        var apiKeyConfigured = TryGetOpenXblApiKey(out _);
        var accountConfigured = apiKeyConfigured && !string.IsNullOrWhiteSpace(_settings.XboxUserId);
        var webhookConfigured = TryGetWebhook(out _);
        var relayRunning = _services.RelayCoordinator.IsRunning;
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
            SetupXboxStatus.Text = "✓ API key stored securely  •  Leave the field blank and select Save and connect to retry";
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

        if (webhookConfigured)
        {
            SetStatus(DiscordStatusText, "Connected", StatusTone.Success);
            DiscordStatusDetail.Text = "Webhook stored with Windows encryption";
            SetupWebhookStatus.Text = "✓ Webhook stored securely  •  Leave the field blank to retest it";
            SetupWebhookStatus.Foreground = Brush("AccentBrush");
            SettingsWebhookStatus.Text = "A Discord webhook is configured and encrypted for this Windows account.";
        }
        else
        {
            SetStatus(DiscordStatusText, "Not connected", StatusTone.Warning);
            DiscordStatusDetail.Text = "Complete step 2 in Guided setup";
            SetupWebhookStatus.Text = "Status: not connected";
            SetupWebhookStatus.Foreground = Brush("WarningBrush");
            SettingsWebhookStatus.Text = "No usable Discord webhook is configured.";
        }

        var relayLabel = relayRunning
            ? string.IsNullOrWhiteSpace(lastError) ? "Monitoring" : "Retrying"
            : "Stopped";
        SetStatus(
            RelayStatusText,
            relayLabel,
            relayRunning && string.IsNullOrWhiteSpace(lastError) ? StatusTone.Success : StatusTone.Warning);

        SidebarMonitorStatus.Text = relayRunning
            ? string.IsNullOrWhiteSpace(lastError) ? "● Monitoring Xbox" : "● Retrying sync"
            : "○ Setup required";
        SidebarMonitorStatus.Foreground = Brush(
            relayRunning && string.IsNullOrWhiteSpace(lastError) ? "AccentBrush" : "WarningBrush");

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
            $"Account monitor: {(relayRunning ? "running" : "not running")}",
            $"Last successful sync: {lastSyncDiagnostic}",
            $"Last sync error: {(string.IsNullOrWhiteSpace(lastError) ? "none" : lastError)}",
            $"Discord: {(webhookConfigured ? "configured" : "not configured")}",
            $"Polling interval: {Math.Clamp(_settings.PollIntervalSeconds, 60, 3600)} seconds",
            $"Install context: {(StartupService.IsPackaged() ? "MSIX packaged" : "classic Windows app")}",
            $"Installer handoff: {_services.Paths.PendingInstallerSetupFile} (deleted after durable import)",
            $"Local data: {_services.Paths.DataDirectory}");
    }

    public void PrepareForExit()
    {
        _isExiting = true;
        _services.ActivityLog.EntryAdded -= OnActivityEntryAdded;
        _services.RelayCoordinator.StatusChanged -= OnRelayStatusChanged;
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
                "Xbox account sync continues in the notification area. Use the tray icon to reopen or exit.",
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
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.2.1";
        AboutVersionText.Text = $"Version {version} beta";

        SetupDisplayNameTextBox.Text = _settings.DisplayName;
        SetupStartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        SetupStartMinimizedCheckBox.IsChecked = _settings.StartMinimized;

        SettingsDisplayNameTextBox.Text = _settings.DisplayName;
        SettingsDiscordUsernameTextBox.Text = _settings.DiscordUsername;
        SettingsRareOnlyCheckBox.IsChecked = _settings.PostRareOnly;
        SettingsRawDetailsCheckBox.IsChecked = _settings.IncludeRawDetailsWhenUncertain;
        SettingsStartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
        SettingsStartMinimizedCheckBox.IsChecked = _settings.StartMinimized;
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

    private async void SaveAndTestOpenXbl_Click(object sender, RoutedEventArgs e) =>
        await SaveAndTestOpenXblAsync(sender, SetupXboxApiKeyPasswordBox.Password, SetupXboxStatus);

    private async void SaveAndTestSettingsOpenXbl_Click(object sender, RoutedEventArgs e) =>
        await SaveAndTestOpenXblAsync(sender, SettingsXboxApiKeyPasswordBox.Password, SettingsXboxStatus);

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
            var accountResult = await _services.OpenXblClient.GetAccountAsync(apiKey);
            if (!accountResult.Success || accountResult.Account is null)
            {
                statusTarget.Text = $"Status: {accountResult.Message}";
                statusTarget.Foreground = Brush("ErrorBrush");
                _services.ActivityLog.Warning(accountResult.Message);
                return;
            }

            var titleProgressResult = await _services.OpenXblClient.GetTitleProgressAsync(
                apiKey,
                accountResult.Account.Xuid);
            if (!titleProgressResult.Success || titleProgressResult.Titles is null)
            {
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

            SetupXboxApiKeyPasswordBox.Clear();
            SettingsXboxApiKeyPasswordBox.Clear();
            PopulateControls();
            RefreshStatus();

            var baselineNote = needsBaseline
                ? " Earlier unlocks were baselined and will not be posted."
                : " The existing sync position was preserved.";
            statusTarget.Text = $"Status: connected as {accountResult.Account.Gamertag}.{baselineNote}";
            statusTarget.Foreground = Brush("AccentBrush");
            _services.ActivityLog.Success("OpenXBL account connection verified. Existing achievements will not be reposted.");

            if (_settings.SetupCompleted && TryGetWebhook(out _))
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
        var value = SetupWebhookPasswordBox.Password;
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
            SetupWebhookPasswordBox.Clear();

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
        if (!TryGetOpenXblApiKey(out _) || string.IsNullOrWhiteSpace(_settings.XboxUserId))
        {
            ShowMessage("Complete step 1 and connect an Xbox account through OpenXBL before finishing setup.", MessageBoxImage.Warning);
            return;
        }

        if (!TryGetWebhook(out _))
        {
            ShowMessage("Complete step 2 and save a valid Discord webhook before finishing setup.", MessageBoxImage.Warning);
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            var startWithWindows = SetupStartWithWindowsCheckBox.IsChecked == true;
            _settings = _settings with
            {
                DisplayName = NormalizePlayerName(SetupDisplayNameTextBox.Text),
                StartWithWindows = startWithWindows,
                StartMinimized = SetupStartMinimizedCheckBox.IsChecked == true,
                SetupCompleted = true
            };

            await _services.SettingsStore.SaveAsync(_settings);
            var startupApplied = await _services.StartupService.SetEnabledAsync(startWithWindows);
            await _services.RelayCoordinator.StartAsync();
            PopulateControls();
            RefreshStatus();
            MainTabs.SelectedIndex = 0;
            _services.ActivityLog.Success("Guided setup completed. Xbox account monitoring is active.");

            var startupNote = startWithWindows && !startupApplied
                ? Environment.NewLine + Environment.NewLine + "Windows did not enable startup automatically. You can enable Achievement Relay in Settings > Apps > Startup."
                : string.Empty;
            ShowMessage(
                "Setup is complete. Achievement Relay checks the connected Xbox account about once a minute while it runs." + startupNote,
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
        var replacement = SettingsWebhookPasswordBox.Password;
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
        var replacement = SettingsWebhookPasswordBox.Password;
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
                PostRareOnly = SettingsRareOnlyCheckBox.IsChecked == true,
                IncludeRawDetailsWhenUncertain = SettingsRawDetailsCheckBox.IsChecked == true,
                StartWithWindows = startWithWindows,
                StartMinimized = SettingsStartMinimizedCheckBox.IsChecked == true
            };

            await _services.SettingsStore.SaveAsync(_settings);
            var startupApplied = await _services.StartupService.SetEnabledAsync(startWithWindows);
            SettingsWebhookPasswordBox.Clear();
            PopulateControls();
            if (_settings.SetupCompleted && TryGetOpenXblApiKey(out _) && TryGetWebhook(out _))
            {
                await _services.RelayCoordinator.StartAsync();
            }

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
            _settings = _settings with
            {
                ProtectedOpenXblApiKey = string.Empty,
                XboxUserId = string.Empty,
                XboxGamertag = string.Empty,
                SetupCompleted = false
            };
            await _services.SettingsStore.SaveAsync(_settings);
            await _services.SyncStateStore.ClearAsync();
            SetupXboxApiKeyPasswordBox.Clear();
            SettingsXboxApiKeyPasswordBox.Clear();
            _services.ActivityLog.Info("Saved OpenXBL connection removed.");
            PopulateControls();
            RefreshStatus();
            MainTabs.SelectedIndex = 1;
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
            _settings = _settings with { ProtectedWebhookUrl = string.Empty, SetupCompleted = false };
            await _services.SettingsStore.SaveAsync(_settings);
            SetupWebhookPasswordBox.Clear();
            SettingsWebhookPasswordBox.Clear();
            _services.ActivityLog.Info("Saved Discord webhook removed.");
            RefreshStatus();
            MainTabs.SelectedIndex = 1;
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void SyncNow_Click(object sender, RoutedEventArgs e)
    {
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
            $"Account monitor running: {_services.RelayCoordinator.IsRunning}",
            $"Last successful sync: {(lastSync is null ? "not yet" : lastSync.Value.ToString("O"))}",
            $"Last sync error: {_services.RelayCoordinator.LastSyncError ?? "none"}",
            $"Discord configured: {TryGetWebhook(out _)}",
            $"Setup completed: {_settings.SetupCompleted}",
            $"Data folder: {_services.Paths.DataDirectory}");

        System.Windows.Clipboard.SetText(summary);
        ShowMessage(
            "Support summary copied. It does not contain the API key, Xbox user ID, gamertag, webhook URL or token.",
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

    private string NormalizePlayerName(string value) =>
        string.IsNullOrWhiteSpace(value) ? _settings.XboxGamertag : value.Trim();

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
            _ => Brush("TextBrush")
        };
    }

    private System.Windows.Media.Brush Brush(string resourceName) =>
        (System.Windows.Media.Brush)FindResource(resourceName);

    private enum StatusTone
    {
        Success,
        Warning,
        Error
    }
}
