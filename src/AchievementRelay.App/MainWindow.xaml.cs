using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        var access = _services.NotificationListener.GetAccessState();
        var webhookConfigured = TryGetWebhook(out _);
        var relayRunning = _services.RelayCoordinator.IsRunning && access == NotificationAccessState.Allowed;

        switch (access)
        {
            case NotificationAccessState.Allowed:
                SetStatus(ListenerStatusText, "Allowed", StatusTone.Success);
                ListenerStatusDetail.Text = "Xbox notifications can be read";
                SetupAccessStatus.Text = "Status: access granted";
                SetupAccessStatus.Foreground = Brush("AccentBrush");
                break;
            case NotificationAccessState.Denied:
                SetStatus(ListenerStatusText, "Blocked", StatusTone.Error);
                ListenerStatusDetail.Text = "Enable access in Windows Settings";
                SetupAccessStatus.Text = "Status: blocked by Windows";
                SetupAccessStatus.Foreground = Brush("ErrorBrush");
                break;
            case NotificationAccessState.Unspecified:
                SetStatus(ListenerStatusText, "Action needed", StatusTone.Warning);
                ListenerStatusDetail.Text = "Complete step 1 in Guided setup";
                SetupAccessStatus.Text = "Status: permission not requested";
                SetupAccessStatus.Foreground = Brush("WarningBrush");
                break;
            default:
                SetStatus(ListenerStatusText, "Unavailable", StatusTone.Error);
                ListenerStatusDetail.Text = "Install the packaged app to enable access";
                SetupAccessStatus.Text = "Status: unavailable in this app context";
                SetupAccessStatus.Foreground = Brush("ErrorBrush");
                break;
        }

        if (webhookConfigured)
        {
            SetStatus(DiscordStatusText, "Connected", StatusTone.Success);
            DiscordStatusDetail.Text = "Webhook stored with Windows encryption";
            SetupWebhookStatus.Text = "Status: webhook configured";
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

        SetStatus(
            RelayStatusText,
            relayRunning ? "Monitoring" : "Stopped",
            relayRunning ? StatusTone.Success : StatusTone.Warning);
        SidebarMonitorStatus.Text = relayRunning ? "● Monitoring Xbox" : "○ Setup required";
        SidebarMonitorStatus.Foreground = Brush(relayRunning ? "AccentBrush" : "WarningBrush");

        DiagnosticsStatusText.Text = string.Join(
            Environment.NewLine,
            $"Notification access: {access}",
            $"Listener: {(relayRunning ? "running" : "not running")}",
            $"Discord: {(webhookConfigured ? "configured" : "not configured")}",
            $"Install context: {(StartupService.IsPackaged() ? "MSIX packaged" : "unpackaged development build")}",
            $"Local data: {_services.Paths.DataDirectory}");
    }

    public void PrepareForExit()
    {
        _isExiting = true;
        _services.ActivityLog.EntryAdded -= OnActivityEntryAdded;
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
                "Xbox achievements will continue to be relayed. Use the tray icon to reopen or exit.",
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

        var icon = extractedIcon ?? System.Drawing.SystemIcons.Application;

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "Achievement Relay",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void PopulateControls()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        AboutVersionText.Text = $"Version {version} alpha";

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

    private async void GrantNotificationAccess_Click(object sender, RoutedEventArgs e)
    {
        SetButtonBusy(sender, true);
        try
        {
            var access = await _services.NotificationListener.RequestAccessAsync();
            if (access == NotificationAccessState.Allowed)
            {
                await _services.RelayCoordinator.StartAsync();
            }
            else if (access == NotificationAccessState.Denied)
            {
                ShowMessage(
                    "Windows did not grant notification access. Open Windows Settings, allow Achievement Relay under notification access, then return and try again.",
                    MessageBoxImage.Warning);
                OpenExternal("ms-settings:privacy-notifications");
            }

            RefreshStatus();
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private void OpenNotificationSettings_Click(object sender, RoutedEventArgs e) =>
        OpenExternal("ms-settings:notifications");

    private void OpenDiscordWebhookGuide_Click(object sender, RoutedEventArgs e) =>
        OpenExternal(DiscordWebhookGuideUrl);

    private async void SaveAndTestWebhook_Click(object sender, RoutedEventArgs e)
    {
        var value = SetupWebhookPasswordBox.Password;
        if (!WebhookUrlValidator.TryNormalize(value, out var webhookUri, out var error) || webhookUri is null)
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
        if (_services.NotificationListener.GetAccessState() != NotificationAccessState.Allowed)
        {
            ShowMessage("Complete step 1 and grant Windows notification access before finishing setup.", MessageBoxImage.Warning);
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
                DisplayName = SetupDisplayNameTextBox.Text.Trim(),
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
            _services.ActivityLog.Success("Guided setup completed.");

            var startupNote = startWithWindows && !startupApplied
                ? Environment.NewLine + Environment.NewLine + "Windows did not enable startup automatically. You can enable Achievement Relay in Settings > Apps > Startup."
                : string.Empty;
            ShowMessage("Setup is complete. Keep Achievement Relay running in the notification area while you play." + startupNote, MessageBoxImage.Information);
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
                SourceApplication = "Achievement Relay",
                SourcePackageFamilyName = "local.sample",
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
        AppSettings settingsForTest;
        var replacement = SettingsWebhookPasswordBox.Password;
        if (!string.IsNullOrWhiteSpace(replacement))
        {
            if (!WebhookUrlValidator.TryNormalize(replacement, out webhookUri, out var error) || webhookUri is null)
            {
                ShowMessage(error ?? "The webhook URL is invalid.", MessageBoxImage.Warning);
                return;
            }

            settingsForTest = _settings with
            {
                DiscordUsername = NormalizeWebhookName(SettingsDiscordUsernameTextBox.Text)
            };
        }
        else
        {
            settingsForTest = _settings with
            {
                DiscordUsername = NormalizeWebhookName(SettingsDiscordUsernameTextBox.Text)
            };
            if (!TryGetWebhook(out webhookUri) || webhookUri is null)
            {
                ShowMessage("Paste a Discord webhook URL first.", MessageBoxImage.Warning);
                return;
            }
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
                DisplayName = SettingsDisplayNameTextBox.Text.Trim(),
                PostRareOnly = SettingsRareOnlyCheckBox.IsChecked == true,
                IncludeRawDetailsWhenUncertain = SettingsRawDetailsCheckBox.IsChecked == true,
                StartWithWindows = startWithWindows,
                StartMinimized = SettingsStartMinimizedCheckBox.IsChecked == true
            };

            await _services.SettingsStore.SaveAsync(_settings);
            var startupApplied = await _services.StartupService.SetEnabledAsync(startWithWindows);
            SettingsWebhookPasswordBox.Clear();
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
            _settings = _settings with { ProtectedWebhookUrl = string.Empty };
            await _services.SettingsStore.SaveAsync(_settings);
            SetupWebhookPasswordBox.Clear();
            SettingsWebhookPasswordBox.Clear();
            _services.ActivityLog.Info("Saved Discord webhook removed.");
            RefreshStatus();
        }
        finally
        {
            SetButtonBusy(sender, false);
        }
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (_services.NotificationListener.GetAccessState() != NotificationAccessState.Allowed)
        {
            ShowMessage("Grant notification access in Guided setup before scanning.", MessageBoxImage.Warning);
            MainTabs.SelectedIndex = 1;
            return;
        }

        SetButtonBusy(sender, true);
        try
        {
            await _services.RelayCoordinator.StartAsync();
            var count = await _services.RelayCoordinator.RescanAsync();
            _services.ActivityLog.Info($"Re-scan found {count} Xbox notification{(count == 1 ? string.Empty : "s")} in Notification Center.");
            RefreshStatus();
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
        var summary = string.Join(
            Environment.NewLine,
            "Achievement Relay support summary",
            $"Version: {version}",
            $"Generated: {DateTimeOffset.Now:O}",
            $"Windows: {Environment.OSVersion}",
            $"Packaged: {StartupService.IsPackaged()}",
            $"Notification access: {_services.NotificationListener.GetAccessState()}",
            $"Listener running: {_services.RelayCoordinator.IsRunning}",
            $"Discord configured: {TryGetWebhook(out _)}",
            $"Setup completed: {_settings.SetupCompleted}",
            $"Data folder: {_services.Paths.DataDirectory}");

        System.Windows.Clipboard.SetText(summary);
        ShowMessage("Support summary copied. It does not contain your webhook URL or token.", MessageBoxImage.Information);
    }

    private void OpenPrivacyPolicy_Click(object sender, RoutedEventArgs e)
    {
        var localPrivacyPolicy = Path.Combine(AppContext.BaseDirectory, "PRIVACY.md");
        OpenExternal(File.Exists(localPrivacyPolicy) ? localPrivacyPolicy : $"{GitHubUrl}/blob/main/PRIVACY.md");
    }

    private void OpenGitHub_Click(object sender, RoutedEventArgs e) => OpenExternal(GitHubUrl);

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
