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
        var installerImport = await _services.InstallerSetupImporter.TryImportAsync(settings);
        settings = installerImport.Settings;
        _mainWindow = new MainWindow(_services, settings);
        MainWindow = _mainWindow;

        if (installerImport.Found)
        {
            if (installerImport.Completed)
            {
                _services.ActivityLog.Success(installerImport.Message);
                var startupApplied = await _services.StartupService.SetEnabledAsync(settings.StartWithWindows);
                if (settings.StartWithWindows && !startupApplied)
                {
                    _services.ActivityLog.Warning(
                        "Installer setup completed, but Windows did not enable automatic startup. Enable Achievement Relay in Windows Startup Apps.");
                }
            }
            else
            {
                _services.ActivityLog.Warning(installerImport.Message);
            }
        }

        var relayStarted = settings.SetupCompleted && await _services.RelayCoordinator.StartAsync();
        var setupReady = settings.SetupCompleted && relayStarted;
        var startMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase) &&
                             setupReady &&
                             settings.StartMinimized;

        if (!startMinimized)
        {
            _mainWindow.Show();
            if (!setupReady)
            {
                _mainWindow.ShowSetup();
            }
        }

        _mainWindow.RefreshStatus();
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
