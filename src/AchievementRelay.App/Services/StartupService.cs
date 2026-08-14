using System.Runtime.InteropServices;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace AchievementRelay.App.Services;

public sealed class StartupService(ActivityLog activityLog)
{
    private const string StartupTaskId = "AchievementRelayStartup";
    private const string RegistryValueName = "AchievementRelay";
    private const string RegistryRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public async Task<bool> SetEnabledAsync(bool enabled)
    {
        if (IsPackaged())
        {
            try
            {
                var startupTask = await StartupTask.GetAsync(StartupTaskId);
                if (!enabled)
                {
                    startupTask.Disable();
                    return true;
                }

                var state = await startupTask.RequestEnableAsync();
                return state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or COMException)
            {
                activityLog.Warning($"Windows Startup Apps could not be updated: {exception.Message}");
                return false;
            }
        }

        return SetRegistryFallback(enabled);
    }

    public async Task<bool> IsEnabledAsync()
    {
        if (IsPackaged())
        {
            try
            {
                var startupTask = await StartupTask.GetAsync(StartupTaskId);
                return startupTask.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
            }
            catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException or COMException)
            {
                activityLog.Warning($"Windows Startup Apps state could not be read: {exception.Message}");
                return false;
            }
        }

        using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, writable: false);
        return key?.GetValue(RegistryValueName) is string;
    }

    public static bool IsPackaged()
    {
        try
        {
            return Package.Current.Id.Name.Length > 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static bool SetRegistryFallback(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryRunKey, writable: true)
            ?? throw new InvalidOperationException("Windows could not open the current-user Startup registry key.");
        if (!enabled)
        {
            key.DeleteValue(RegistryValueName, throwOnMissingValue: false);
            return true;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        key.SetValue(RegistryValueName, $"\"{executablePath}\" --minimized", RegistryValueKind.String);
        return true;
    }
}
