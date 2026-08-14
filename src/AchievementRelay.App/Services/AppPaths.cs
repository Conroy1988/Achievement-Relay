using System.IO;

namespace AchievementRelay.App.Services;

public sealed class AppPaths
{
    public AppPaths()
    {
        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AchievementRelay");

        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public string EventLedgerFile => Path.Combine(DataDirectory, "processed-events.json");

    public string LogFile => Path.Combine(DataDirectory, "achievement-relay.log");
}
