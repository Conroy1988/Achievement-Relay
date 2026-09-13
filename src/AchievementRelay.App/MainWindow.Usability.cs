using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Text.Json;

namespace AchievementRelay.App;

public partial class MainWindow
{
    private string? _savedEditorState;
    private bool _trackingEdits;
    private bool _savingPreferences;
    private DateTimeOffset? _lastAccountSync;
    private string? _accountSyncProblem;
    private System.Windows.Data.ListCollectionView? _activityView;

    internal void SelectReviewPage(int page) => NavigateTo(page);
    internal void VerifyDraftProtection()
    {
        var original = SettingsDisplayNameTextBox.Text;
        SettingsDisplayNameTextBox.Text = "Unsaved review edit";
        if (!HasSettingsDraft) throw new InvalidOperationException("An unsaved settings edit was not detected.");
        if (!BlockConnectionDraft()) throw new InvalidOperationException("A connection action could replace an unsaved draft.");
        NavigateTo(0);
        if (!HasSettingsDraft) throw new InvalidOperationException("Navigation lost the draft.");
        DiscardSettings_Click(this, new RoutedEventArgs());
        if (HasSettingsDraft || SettingsDisplayNameTextBox.Text != original) throw new InvalidOperationException("Discard did not restore saved settings.");
        SettingsXboxApiKeyPasswordBox.Password = "synthetic-unsaved-key";
        if (PendingXboxKey() != "synthetic-unsaved-key" || BlockConnectionDraft(allowXboxKey: true)) throw new InvalidOperationException("A key-only draft cannot reach verification.");
        SettingsDisplayNameTextBox.Text = "Another draft";
        if (!BlockConnectionDraft(allowXboxKey: true)) throw new InvalidOperationException("Key verification could lose another draft.");
        DiscardSettings_Click(this, new RoutedEventArgs());
    }
    private string EditorState() => JsonSerializer.Serialize(new object?[] {
        SettingsDisplayNameTextBox.Text, SettingsDiscordUsernameTextBox.Text,
        GetSecretValue(SettingsXboxApiKeyPasswordBox, SettingsXboxApiKeyRevealTextBox),
        GetSecretValue(SettingsWebhookPasswordBox, SettingsWebhookRevealTextBox),
        SettingsSteamEnabledCheckBox.IsChecked, SettingsRareOnlyCheckBox.IsChecked,
        SettingsRawDetailsCheckBox.IsChecked, SettingsAchievementOverlayEnabledCheckBox.IsChecked,
        SettingsOverlayAnimationCheckBox.IsChecked, SettingsOverlayReducedMotionCheckBox.IsChecked,
        SettingsOverlayFollowWindowsCheckBox.IsChecked, SettingsOverlaySoundCheckBox.IsChecked,
        SettingsOverlayVolumeSlider.Value, SettingsStartWithWindowsCheckBox.IsChecked,
        SettingsStartMinimizedCheckBox.IsChecked });

    private bool HasSettingsDraft => _savedEditorState is not null && EditorState() != _savedEditorState;

    private void InitializeUsability(bool previewOnly)
    {
        _savedEditorState = EditorState();
        _activityView = new System.Windows.Data.ListCollectionView(_activity);
        _activityView.Filter = item => item is Services.ActivityEntry entry &&
            entry.Message.Contains(ActivitySearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) &&
            (ActivityAttentionOnly.IsChecked != true || entry.Level is Services.ActivityLevel.Warning or Services.ActivityLevel.Error);
        ActivityList.ItemsSource = _activityView;
        _trackingEdits = true;
        MainTabs.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => RefreshDraftStatus()));
        MainTabs.AddHandler(PasswordBox.PasswordChangedEvent, new RoutedEventHandler((_, _) => RefreshDraftStatus()));
        MainTabs.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler((_, _) => RefreshDraftStatus()));
        MainTabs.AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler((_, _) => RefreshDraftStatus()));
        SettingsOverlayVolumeSlider.ValueChanged += (_, _) => RefreshDraftStatus();
        PreviewKeyDown += (_, e) => {
            if (e.Key == Key.F1) { NavigateTo(4); e.Handled = true; }
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control && MainTabs.SelectedIndex == 3) {
                SaveSettings_Click(SavePreferencesButton, new RoutedEventArgs()); e.Handled = true;
            }
        };
        if (!previewOnly) {
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(Width, workArea.Width);
            Height = Math.Min(Height, workArea.Height);
            MinHeight = Math.Min(MinHeight, workArea.Height);
            MinWidth = Math.Min(MinWidth, workArea.Width);
        }
        RefreshDraftStatus();
    }

    private void RefreshDraftStatus()
    {
        if (!_trackingEdits) return;
        var dirty = HasSettingsDraft;
        SettingsSaveStatus.Text = dirty ? "Unsaved changes — Save settings applies your preferences. Xbox keys use Verify and save key." : "Your settings are saved.";
        SettingsNavButton.ToolTip = dirty ? "Settings · unsaved changes" : "Connections, preferences and this PC";
        RefreshAccountSummary();
    }

    private bool BlockConnectionDraft(bool allowXboxKey = false)
    {
        if (!HasSettingsDraft) return false;
        if (allowXboxKey && _savedEditorState is not null) {
            using var saved = JsonDocument.Parse(_savedEditorState);
            using var current = JsonDocument.Parse(EditorState());
            if (Enumerable.Range(0, saved.RootElement.GetArrayLength()).Where(i => i != 2)
                .All(i => saved.RootElement[i].GetRawText() == current.RootElement[i].GetRawText())) return false;
        }
        MainTabs.SelectedIndex = 3; UpdateNavigationState();
        SettingsSaveStatus.Text = "Save or discard your other Settings edits before changing connections. An unverified Xbox key is kept when you save preferences.";
        return true;
    }
    private string? PendingXboxKey()
    {
        if (_savedEditorState is null) return null;
        using var saved = JsonDocument.Parse(_savedEditorState);
        var key = GetSecretValue(SettingsXboxApiKeyPasswordBox, SettingsXboxApiKeyRevealTextBox);
        return saved.RootElement[2].GetString() == key ? null : key;
    }
    private bool ConfirmExitWithDraft()
    {
        if (!HasSettingsDraft) return true;
        ShowFromTray(); NavigateTo(3);
        return System.Windows.MessageBox.Show(this, "Quit Relay and discard unsaved Settings edits? Choose No to return and save them. Closing the window instead keeps the draft in the tray.", "Unsaved settings", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }
    private void HomeLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var compact = e.NewSize.Width < 950;
        HomeLayout.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 12);
        HomeLayout.ColumnDefinitions[2].Width = new GridLength(compact ? 0 : 310);
        Grid.SetColumn(HomeConnectionsColumn, compact ? 0 : 2);
        Grid.SetRow(HomeConnectionsColumn, compact ? 1 : 0);
    }
    private void ResetOverlayDefaults_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AchievementRelay.Core.Models.AppSettings();
        SettingsAchievementOverlayEnabledCheckBox.IsChecked = defaults.AchievementOverlayEnabled;
        SettingsOverlayAnimationCheckBox.IsChecked = defaults.AchievementOverlayAnimationEnabled;
        SettingsOverlayReducedMotionCheckBox.IsChecked = defaults.AchievementOverlayReducedMotion;
        SettingsOverlayFollowWindowsCheckBox.IsChecked = defaults.AchievementOverlayFollowWindowsMotion;
        SettingsOverlaySoundCheckBox.IsChecked = defaults.AchievementOverlaySoundEnabled;
        SettingsOverlayVolumeSlider.Value = defaults.AchievementOverlayVolume;
        RefreshDraftStatus();
    }
    private void ActivityFilterChanged(object sender, RoutedEventArgs e) => _activityView?.Refresh();
    private void SettingsSection_Click(object sender, RoutedEventArgs e)
    {
        var tag = (sender as System.Windows.Controls.Button)?.Tag?.ToString();
        FrameworkElement target = tag switch { "xbox" => SettingsXboxApiKeyPasswordBox, "discord" => SettingsWebhookPasswordBox, "windows" => SettingsStartWithWindowsCheckBox, _ => SettingsAchievementOverlayEnabledCheckBox };
        target.BringIntoView(); target.Focus();
    }
    private void DiscardSettings_Click(object sender, RoutedEventArgs e)
    {
        PopulateControls();
        SettingsSaveStatus.Text = "Unsaved edits discarded. Saved settings restored.";
    }

    private void RefreshAccountSummary()
    {
        if (HomeAccountSummary is null) return;
        HomeAccountSummary.Text = !_services.AccountCloud.IsConnected ? "Optional · use Relay locally, or sign in to sync your PCs."
            : !_services.AccountCloud.HasRecoveryKey ? "Signed in · unlock with your recovery key to sync."
            : _accountSyncRunning ? "Syncing your account…"
            : _accountSyncProblem ?? ((_lastAccountSync ?? _services.AccountSync.LastSuccessfulSync) is { } time ? $"Last synced {time.ToLocalTime():g}." : "Account unlocked · ready to sync.");
        if (_services.AccountCloud.IsConnected) HomeAccountSummary.Text = _services.AccountCloud.AccountDisplayName + " · " + HomeAccountSummary.Text;
        if (HasSettingsDraft) HomeAccountSummary.Text += " Save or discard your settings edits before syncing.";
    }

    private void OpenPresentationControls_Click(object sender, RoutedEventArgs e)
    {
        ShowCompanion_Click(sender, e);
        _companion?.SelectSection("Presentation");
    }
}
