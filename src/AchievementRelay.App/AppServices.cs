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
        AchievementDeliveryService = new AchievementDeliveryService(
            WebhookProtector,
            EventLedger,
            WebhookClient,
            ActivityLog);
        OpenXblClient = new OpenXblClient();
        SyncStateStore = new XboxSyncStateStore(Paths);
        SteamSyncStateStore = new SteamSyncStateStore(Paths);
        SteamGameDetector = new SteamGameDetector();
        SteamRarityClient = new SteamRarityClient();
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
            AchievementDeliveryService,
            ActivityLog);
        SteamMonitorCoordinator = new SteamMonitorCoordinator(
            SteamGameDetector,
            SteamSyncStateStore,
            SteamRarityClient,
            SettingsStore,
            AchievementDeliveryService,
            ActivityLog);
    }

    public AppPaths Paths { get; }

    public ActivityLog ActivityLog { get; }

    public SecureWebhookProtector WebhookProtector { get; }

    public SettingsStore SettingsStore { get; }

    public EventLedger EventLedger { get; }

    public DiscordWebhookClient WebhookClient { get; }

    public AchievementDeliveryService AchievementDeliveryService { get; }

    public OpenXblClient OpenXblClient { get; }

    public XboxSyncStateStore SyncStateStore { get; }

    public SteamSyncStateStore SteamSyncStateStore { get; }

    public SteamGameDetector SteamGameDetector { get; }

    public SteamRarityClient SteamRarityClient { get; }

    public InstallerSetupImporter InstallerSetupImporter { get; }

    public StartupService StartupService { get; }

    public RelayCoordinator RelayCoordinator { get; }

    public SteamMonitorCoordinator SteamMonitorCoordinator { get; }

    public void Dispose()
    {
        SteamMonitorCoordinator.Dispose();
        RelayCoordinator.Dispose();
        AchievementDeliveryService.Dispose();
        OpenXblClient.Dispose();
        SteamRarityClient.Dispose();
        WebhookClient.Dispose();
    }
}
