using System.Windows;
using System.Windows.Threading;

namespace AchievementRelay.App;

public partial class MainWindow
{
    private DispatcherTimer? _accountTimer;
    private bool _accountSyncRunning;
    private void InitializeAccountSync()
    {
        _accountTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _accountTimer.Tick += async (_, _) =>
        {
            if (_isExiting || IsVisible || _companion is not null || !_services.AccountCloud.HasRecoveryKey || _accountSyncRunning) return;
            try { await SyncAccountAsync(); }
            catch (Exception) { _services.ActivityLog.Warning("Account sync unavailable. Local settings and history are kept; Relay will retry later."); }
        };
        _accountTimer.Start();
        Closed += (_, _) => _accountTimer.Stop();
    }
    private void ShowAccount_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AccountWindow(_services, SyncAccountAsync) { Owner = this };
        dialog.ShowDialog();
    }
    private async Task SyncAccountAsync()
    {
        if (_accountSyncRunning) return;
        _accountSyncRunning = true;
        try
        {
            _settings = await _services.AccountSync.SyncAsync();
            if (!_services.UpdateService.IsUpdateRequired && _settings.SetupCompleted && !string.IsNullOrEmpty(_settings.ProtectedWebhookUrl))
            {
                if (!string.IsNullOrEmpty(_settings.ProtectedOpenXblApiKey)) await _services.RelayCoordinator.StartAsync();
                else await _services.RelayCoordinator.StopAsync();
                if (_settings.SteamEnabled) await _services.SteamMonitorCoordinator.StartAsync();
            }
            PopulateControls();
            RefreshStatus();
            _services.ActivityLog.Success("Relay account synced.");
        }
        finally { _accountSyncRunning = false; }
    }
}
