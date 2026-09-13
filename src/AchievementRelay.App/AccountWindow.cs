using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace AchievementRelay.App;

public sealed class AccountWindow : Window
{
    private readonly AppServices _services;
    private readonly Func<Task> _sync;
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private readonly PasswordBox _recovery = new() { Margin = new Thickness(0, 6, 0, 8) };
    private readonly StackPanel _actions = new();
    private readonly CancellationTokenSource _closed = new();
    public AccountWindow(AppServices services, Func<Task> sync)
    {
        _services = services; _sync = sync;
        Title = "Relay account"; Width = 570; Height = 650; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 26, 33)); Foreground = System.Windows.Media.Brushes.White;
        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "Your Relay account", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = "Optional encrypted sync for settings, connections, pins and recent achievement history. Device positions, Windows startup and artwork stay local.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) });
        panel.Children.Add(_status); panel.Children.Add(_actions);
        Add("Continue with Discord", async () => { await services.AccountCloud.SignInAsync(_closed.Token); _status.Text = "Signed in. Create a recovery key for a new account or enter your existing key below."; });
        Add("Create recovery key for a new account", async () =>
        {
            var key = await services.AccountCloud.CreateRecoveryKeyAsync();
            _recovery.Password = key;
            _status.Text = "Recovery key created. Use Copy recovery key and store it safely before syncing. Supabase cannot recover it for you.";
        });
        _actions.Children.Add(new TextBlock { Text = "Recovery key from your first device", Margin = new Thickness(0, 10, 0, 0) });
        _actions.Children.Add(_recovery);
        Add("Unlock with recovery key", async () => { await services.AccountCloud.SetRecoveryKeyAsync(_recovery.Password); _recovery.Clear(); _status.Text = "Account unlocked on this device."; });
        Add("Copy recovery key", () => { System.Windows.Clipboard.SetText(services.AccountCloud.ExportRecoveryKey()); _status.Text = "Recovery key copied. Store it privately; anyone with this key and account access can decrypt your profile."; return Task.CompletedTask; });
        Add("Sync now", async () => { await _sync(); _status.Text = "Account synced. Automatic sync runs every five minutes while Relay is in the tray."; });
        Add("Sign out on this device", async () => { await services.AccountCloud.SignOutAsync(); _recovery.Clear(); _status.Text = "Signed out. Local settings and history are kept. This device now posts independently."; });
        panel.Children.Add(new TextBlock { Text = "Cloud delivery coordination is active while signed in. If the service is unavailable, posts wait. An uncertain send is never automatically repeated. Free-plan limits apply; no paid upgrades are enabled.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 16, 0, 0) });
        _status.Text = services.AccountCloud.IsConnected ? "Signed in" + (services.AccountCloud.HasRecoveryKey ? " and unlocked." : "; enter your recovery key.") : "Using Relay locally. Sign in to enable account sync.";
        Closed += (_, _) => _closed.Cancel();
    }
    private void Add(string title, Func<Task> action)
    {
        var button = new Button { Content = title, Margin = new Thickness(0, 4, 0, 4), Padding = new Thickness(12, 7, 12, 7) };
        _actions.Children.Add(button);
        button.Click += async (_, _) =>
        {
            _actions.IsEnabled = false;
            try { await action(); }
            catch (OperationCanceledException) { _status.Text = "Sign-in or sync timed out. Your local data is kept."; }
            catch (Exception) { _status.Text = "Account operation failed. Check your connection and recovery key, then try again. Local data is kept."; }
            finally { _actions.IsEnabled = true; }
        };
    }
}
