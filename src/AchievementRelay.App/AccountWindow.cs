using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;

namespace AchievementRelay.App;

public sealed class AccountWindow : Window
{
    private readonly AppServices _services;
    private readonly Func<Task> _sync;
    private readonly Func<string> _summary;
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 12, 0, 12) };
    private readonly PasswordBox _recovery = new() { Margin = new Thickness(0, 8, 0, 8) };
    private readonly StackPanel _body = new() { Margin = new Thickness(24) };
    private readonly CheckBox _backup = new() { Content = "I have saved my recovery key somewhere private.", Margin = new Thickness(0, 8, 0, 8) };
    private readonly CancellationTokenSource _closed = new();
    private CancellationTokenSource? _operation;
    private readonly List<(Button Button, Func<bool> Enabled)> _buttons = [];
    private bool _busy;
    private bool _newKey;
    private bool _signingIn;

    public AccountWindow(AppServices services, Func<Task> sync, Func<string>? summary = null)
    {
        _services = services; _sync = sync; _summary = summary ?? (() => "Ready to sync.");
        Style = (Style)FindResource(typeof(Window));
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        Title = "Relay account and sync"; Width = 620; Height = 780; MinWidth = 420; MinHeight = 440;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Content = new ScrollViewer { Background = (System.Windows.Media.Brush)FindResource("WindowSurfaceBrush"), Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Heading("Your Relay account");
        Copy("Optional encrypted sync for your PCs. You can keep using Relay without signing in. Discord login pairs your devices; your posting channel is configured separately in Settings.");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        _body.Children.Add(_status);
        Heading("1. Sign in", 18);
        Add("Continue with Discord", async () => {
            _signingIn = true; RefreshButtons();
            try { await services.AccountCloud.SignInAsync(_operation!.Token); _status.Text = "Signed in as " + services.AccountCloud.AccountDisplayName + ". Next, unlock your profile below."; }
            finally { _signingIn = false; }
        }, () => !services.AccountCloud.HasRecoveryKey, "Waiting for Discord in your browser. Return here after signing in.");
        var cancel = new Button { Content = "Cancel sign-in", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 8) };
        cancel.Click += (_, _) => _operation?.Cancel();
        _body.Children.Add(cancel);
        _buttons.Add((cancel, () => _signingIn));
        Heading("2. Unlock your profile", 18);
        Copy("First PC? Create a recovery key, then save a copy in your password manager. Another PC? Paste the key you saved on your first PC. The cloud service cannot recover this key.");
        Add("First PC: create recovery key", async () => {
            await services.AccountCloud.CreateRecoveryKeyAsync(); _newKey = true; _backup.IsChecked = false;
            _status.Text = "Key created. Copy it and save it privately before your first sync.";
        }, () => services.AccountCloud.IsConnected && !services.AccountCloud.HasRecoveryKey);
        AutomationProperties.SetName(_recovery, "Recovery key from your first PC");
        _body.Children.Add(_recovery);
        Add("Another PC: unlock with saved key", async () => {
            await services.AccountCloud.SetRecoveryKeyAsync(_recovery.Password); _recovery.Clear(); _newKey = false;
            _status.Text = "Profile unlocked. Select Sync now to bring your settings onto this PC.";
        }, () => services.AccountCloud.IsConnected && !services.AccountCloud.HasRecoveryKey && !string.IsNullOrWhiteSpace(_recovery.Password));
        _recovery.PasswordChanged += (_, _) => RefreshButtons();
        Add("Copy recovery key", () => { System.Windows.Clipboard.SetText(services.AccountCloud.ExportRecoveryKey()); _status.Text = "Key copied. Save it privately, then replace your clipboard contents when finished."; return Task.CompletedTask; }, () => services.AccountCloud.HasRecoveryKey);
        _body.Children.Add(_backup); _backup.Checked += (_, _) => RefreshButtons(); _backup.Unchecked += (_, _) => RefreshButtons();
        Heading("3. Sync your PCs", 18);
        Copy("Your connections, presentation and sound preferences, pins and recent history sync. Windows startup, screen position, artwork and the Steam account on this PC stay local. Existing achievements are never posted again by importing history.");
        Add("Sync now", async () => { await _sync(); _status.Text = _summary(); }, () => services.AccountCloud.HasRecoveryKey && (!_newKey || _backup.IsChecked == true), "Syncing your settings and recent history…");
        Copy("Automatic sync runs every five minutes while Relay is in the tray and Companion is closed. Save your edits first. If both PCs change the same preference, the last successful write wins.");
        var details = new StackPanel();
        details.Children.Add(new TextBlock { Text = "Signed-in PCs coordinate posts. If cloud access fails, posting waits. Uncertain sends stay held: check Discord before taking action. Supabase Free limits apply. Account sync remains an optional beta.", Margin = new Thickness(0, 8, 0, 8) });
        details.Children.Add(new TextBlock { Text = "This Windows profile is paired to one Relay account. Account switching and cloud deletion are not available in this version. Losing every copy of the recovery key makes the cloud profile unreadable. Signing out keeps local settings and history and returns this PC to independent posting." });
        _body.Children.Add(new Expander { Header = "Privacy, recovery and delivery details", Content = details, Margin = new Thickness(0, 12, 0, 12) });
        Add("Sign out on this PC", async () => {
            if (System.Windows.MessageBox.Show(this, "Keep local settings and history, remove this PC’s session and key, and return to independent posting? Keep a recovery-key backup to sign in again.", "Sign out of Relay", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            await services.AccountCloud.SignOutAsync(); _recovery.Clear(); _newKey = false;
            _status.Text = "Signed out. Local data is kept; this PC now posts independently.";
        }, () => services.AccountCloud.IsConnected);
        _status.Text = services.AccountCloud.IsConnected ? _summary() : "Using Relay locally. Sign in only if you want to sync PCs.";
        Closing += (_, e) => {
            if (_newKey && _backup.IsChecked != true)
                e.Cancel = System.Windows.MessageBox.Show(this, "You have not confirmed saving a recovery-key backup. Close anyway? You can copy it later while this PC remains unlocked.", "Save your recovery key", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes;
        };
        Closed += (_, _) => { _closed.Cancel(); _operation?.Cancel(); _recovery.Clear(); };
        RefreshButtons();
    }
    private void Heading(string text, double size = 25) => _body.Children.Add(new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) });
    private void Copy(string text) => _body.Children.Add(new TextBlock { Text = text, Margin = new Thickness(0, 4, 0, 8) });
    private void RefreshButtons()
    {
        foreach (var (button, enabled) in _buttons) {
            var cancel = button.Content?.ToString() == "Cancel sign-in";
            button.IsEnabled = (cancel || !_busy) && enabled();
            if (cancel) button.Visibility = _signingIn ? Visibility.Visible : Visibility.Collapsed;
        }
        _recovery.IsEnabled = !_busy && _services.AccountCloud.IsConnected && !_services.AccountCloud.HasRecoveryKey;
        _backup.Visibility = _newKey ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Add(string title, Func<Task> action, Func<bool> enabled, string progress = "Working…")
    {
        var button = new Button { Content = title, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 6) };
        _body.Children.Add(button); _buttons.Add((button, enabled));
        button.Click += async (_, _) => {
            if (_busy) return;
            _busy = true; _operation = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token); RefreshButtons(); _status.Text = progress;
            try { await action(); }
            catch (OperationCanceledException) { _status.Text = "Sign-in cancelled or timed out. Select Continue with Discord to try again."; }
            catch (SocketException) { _status.Text = "Another app is using the sign-in port. Close other Relay sign-in windows, then try again."; }
            catch (Exception ex) when (ex is CryptographicException or FormatException) { _status.Text = "That recovery key did not unlock this profile. Copy the complete key from your first PC and try again."; }
            catch (HttpRequestException) { _status.Text = "Cannot reach your account. Check your internet connection and try again. If this continues, sign in again. Local data is kept."; }
            catch (InvalidOperationException) { _status.Text = "This account could not complete that step. An existing profile needs its original recovery key; this Windows profile cannot switch accounts."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _status.Text = "Relay could not save local account data. Check disk space and access to your Windows profile, then retry."; }
            catch (Exception) { _status.Text = "The account action did not finish. Local data is kept. Retry or open Help for support."; }
            finally { _operation.Dispose(); _operation = null; _busy = false; RefreshButtons(); }
        };
    }
}
