using System.Threading;
using System.Windows;

namespace AchievementRelay.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\AchievementRelay.Application";
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private AppServices? _services;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "Achievement Relay is already running in the notification area.",
                "Achievement Relay",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            _services?.ActivityLog.Error($"Application error: {args.Exception.Message}");
            System.Windows.MessageBox.Show(
                "Achievement Relay encountered an unexpected error. Details were written to the local log.",
                "Achievement Relay",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        _services = new AppServices();
        var settings = await _services.SettingsStore.LoadAsync();
        _mainWindow = new MainWindow(_services, settings);
        MainWindow = _mainWindow;

        var startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) &&
                             settings.SetupCompleted &&
                             settings.StartMinimized;

        if (!startMinimized)
        {
            _mainWindow.Show();
            if (!settings.SetupCompleted)
            {
                _mainWindow.ShowSetup();
            }
        }

        if (_services.NotificationListener.GetAccessState() == Services.NotificationAccessState.Allowed)
        {
            await _services.RelayCoordinator.StartAsync();
            _mainWindow.RefreshStatus();
        }
    }

    public void ExitApplication()
    {
        _mainWindow?.PrepareForExit();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
