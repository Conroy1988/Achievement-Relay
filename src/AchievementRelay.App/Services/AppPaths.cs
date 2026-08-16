using System.IO;

namespace AchievementRelay.App.Services;

public sealed class AppPaths
{
    private const string PendingInstallerSetupFileName = "pending-installer-setup.json";

    public AppPaths()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AchievementRelay");

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        PendingInstallerSetupFile = string.IsNullOrWhiteSpace(userProfile)
            ? Path.Combine(DataDirectory, PendingInstallerSetupFileName)
            : Path.Combine(userProfile, ".achievement-relay", PendingInstallerSetupFileName);
        LegacyPendingInstallerSetupFile = Path.Combine(DataDirectory, PendingInstallerSetupFileName);
        PendingInstallerSetupFiles =
        [
            PendingInstallerSetupFile,
            LegacyPendingInstallerSetupFile
        ];

        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public string EventLedgerFile => Path.Combine(DataDirectory, "processed-events.json");

    public string XboxSyncStateFile => Path.Combine(DataDirectory, "xbox-sync-state.json");

    public string SteamSyncStateFile => Path.Combine(DataDirectory, "steam-sync-state.json");

    /// <summary>
    /// One-time installer handoff outside AppData so MSIX virtualization cannot split the
    /// installer and packaged-app views of the file.
    /// </summary>
    public string PendingInstallerSetupFile { get; }

    public string LegacyPendingInstallerSetupFile { get; }

    public IReadOnlyList<string> PendingInstallerSetupFiles { get; }

    public string LogFile => Path.Combine(DataDirectory, "achievement-relay.log");

    public string UpdatesDirectory => Path.Combine(DataDirectory, "Updates");

    public string UpdateStateFile => Path.Combine(DataDirectory, "update-state.json");
}
