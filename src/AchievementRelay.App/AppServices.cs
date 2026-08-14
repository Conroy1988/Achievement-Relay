using AchievementRelay.App.Services;

namespace AchievementRelay.App;

public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Paths = new AppPaths();
        ActivityLog = new ActivityLog(Paths);
        WebhookProtector = new SecureWebhookProtector();
        SettingsStore = new SettingsStore(Paths);
        EventLedger = new EventLedger(Paths);
        WebhookClient = new DiscordWebhookClient();
        OpenXblClient = new OpenXblClient();
        SyncStateStore = new XboxSyncStateStore(Paths);
        InstallerSetupImporter = new InstallerSetupImporter(
            Paths,
            WebhookProtector,
            SettingsStore,
            SyncStateStore,
            OpenXblClient,
            WebhookClient);
        StartupService = new StartupService(ActivityLog);
        RelayCoordinator = new RelayCoordinator(
            OpenXblClient,
            SettingsStore,
            WebhookProtector,
            SyncStateStore,
            EventLedger,
            WebhookClient,
            ActivityLog);
    }

    public AppPaths Paths { get; }

    public ActivityLog ActivityLog { get; }

    public SecureWebhookProtector WebhookProtector { get; }

    public SettingsStore SettingsStore { get; }

    public EventLedger EventLedger { get; }

    public DiscordWebhookClient WebhookClient { get; }

    public OpenXblClient OpenXblClient { get; }

    public XboxSyncStateStore SyncStateStore { get; }

    public InstallerSetupImporter InstallerSetupImporter { get; }

    public StartupService StartupService { get; }

    public RelayCoordinator RelayCoordinator { get; }

    public void Dispose()
    {
        RelayCoordinator.Dispose();
        OpenXblClient.Dispose();
        WebhookClient.Dispose();
    }
}
