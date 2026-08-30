using System.IO;
using System.Threading;
using System.Windows;
using System.Runtime.InteropServices;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

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

        if (TryExportCollectorCardPreview(e.Args, out var previewExitCode))
        {
            Shutdown(previewExitCode);
            return;
        }

        if (TryExportSignalStripPreview(e.Args, out previewExitCode))
        {
            Shutdown(previewExitCode);
            return;
        }

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

        var requestedMinimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);
        if (!requestedMinimized)
        {
            _mainWindow.ShowHome();
        }

        var updateState = await _services.UpdateService.CheckAsync(force: false);

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

        var automaticUpdaterStarted =
            await _mainWindow.TryStartAutomaticUpdateOnLaunchAsync(updateState);
        if (automaticUpdaterStarted)
        {
            _mainWindow.RefreshStatus();
            _services.UpdateService.StartAutomaticChecks();
            return;
        }

        updateState = _services.UpdateService.Snapshot;
        var updateRequired = updateState.IsRequired;

        var apiKey = _services.WebhookProtector.TryUnprotectOpenXblApiKey(settings.ProtectedOpenXblApiKey);
        var webhookValue = _services.WebhookProtector.TryUnprotect(settings.ProtectedWebhookUrl);
        var webhookConfigured = WebhookUrlValidator.TryNormalize(webhookValue, out _, out _);
        var xboxConfigured = OpenXblApiKeyValidator.TryNormalize(apiKey, out _, out _) &&
                             !string.IsNullOrWhiteSpace(settings.XboxUserId);
        var xboxStarted = !updateRequired && settings.SetupCompleted && webhookConfigured && xboxConfigured &&
                          await _services.RelayCoordinator.StartAsync();
        var steamStarted = !updateRequired && settings.SetupCompleted && webhookConfigured && settings.SteamEnabled &&
                            await _services.SteamMonitorCoordinator.StartAsync();
        var setupReady = settings.SetupCompleted && (xboxStarted || steamStarted);
        var startMinimized = !updateRequired &&
                             requestedMinimized &&
                             setupReady &&
                             settings.StartMinimized;

        if (!startMinimized)
        {
            if (!setupReady)
            {
                if (updateRequired)
                {
                    _mainWindow.ShowRequiredUpdate();
                }
                else
                {
                    _mainWindow.ShowSetup();
                }
            }
            else
            {
                _mainWindow.ShowHome();
            }
        }

        _mainWindow.RefreshStatus();
        _services.UpdateService.StartAutomaticChecks();
    }

    private static bool TryExportCollectorCardPreview(string[] args, out int exitCode)
    {
        exitCode = 0;
        var optionIndex = Array.FindIndex(
            args,
            value => string.Equals(
                value,
                "--export-collector-card-preview",
                StringComparison.OrdinalIgnoreCase));
        if (optionIndex < 0)
        {
            return false;
        }

        if (optionIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[optionIndex + 1]))
        {
            exitCode = 2;
            return true;
        }

        try
        {
            var outputPath = Path.GetFullPath(args[optionIndex + 1]);
            var directory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                exitCode = 2;
                return true;
            }

            Directory.CreateDirectory(directory);
            var card = new DiscordCollectorCardRenderer().RenderGoldFallbackPreview();
            File.WriteAllBytes(outputPath, card.Bytes);
        }
        catch (Exception exception) when (exception is ArgumentException or
                                          ExternalException or
                                          IOException or
                                          InvalidOperationException or
                                          NotSupportedException or
                                          OutOfMemoryException or
                                          PlatformNotSupportedException)
        {
            exitCode = 2;
        }

        return true;
    }

    private static bool TryExportSignalStripPreview(string[] args, out int exitCode)
    {
        exitCode = 0;
        var optionIndex = Array.FindIndex(
            args,
            value => string.Equals(
                value,
                "--export-signal-strip-preview",
                StringComparison.OrdinalIgnoreCase));
        if (optionIndex < 0)
        {
            return false;
        }

        if (optionIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[optionIndex + 1]))
        {
            exitCode = 2;
            return true;
        }

        try
        {
            var outputPath = Path.GetFullPath(args[optionIndex + 1]);
            var directory = Path.GetDirectoryName(outputPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                exitCode = 2;
                return true;
            }

            Directory.CreateDirectory(directory);
            var achievement = new AchievementEvent
            {
                Id = "signal-strip-preview",
                Name = "Ravenous",
                Description = "Unlock a rare achievement during live monitoring.",
                GameName = "Palworld",
                Gamerscore = 30,
                IsRare = true,
                RarityKnown = true,
                RarityPercentage = 4.7,
                PlayerName = "Relay Player",
                SourceProvider = "OpenXBL",
                Platform = "Xbox PC",
                UnlockedAt = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero)
            };
            var presentation = AchievementOverlayPresentation.Create(achievement);
            var preview = AchievementOverlayWindow.RenderPreview(presentation);
            File.WriteAllBytes(outputPath, preview);
        }
        catch (Exception exception) when (exception is ArgumentException or
                                          ExternalException or
                                          IOException or
                                          InvalidOperationException or
                                          NotSupportedException or
                                          OutOfMemoryException or
                                          PlatformNotSupportedException)
        {
            exitCode = 2;
        }

        return true;
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
