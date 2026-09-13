using AchievementRelay.App.Services;

namespace AchievementRelay.App;

public sealed class AppServices : IDisposable
{
    public AppServices(AppPaths? paths = null)
    {
        Paths = paths ?? new AppPaths();
        ActivityLog = new ActivityLog(Paths);
        UpdateService = new AppUpdateService(Paths, ActivityLog);
        WebhookProtector = new SecureWebhookProtector();
        SettingsStore = new SettingsStore(Paths);
        AccountCloud = new AccountCloudClient(Paths);
        AccountSync = new AccountSyncService(this);
        EventLedger = new EventLedger(Paths);
        CompanionJournal = new CompanionJournal(Paths, ActivityLog);
        CompanionLibrary = new CompanionLibrary(Paths);
        WebhookClient = new DiscordWebhookClient();
        ArtworkClient = new AchievementArtworkClient();
        AchievementOverlayService = new AchievementOverlayService(ActivityLog);
        CollectorCardRenderer = new DiscordCollectorCardRenderer();
        AchievementPostComposer = new DiscordAchievementPostComposer(
            ArtworkClient,
            CollectorCardRenderer,
            ActivityLog);
        AchievementDeliveryService = new AchievementDeliveryService(
            WebhookProtector,
            EventLedger,
            WebhookClient,
            AchievementPostComposer,
            AchievementOverlayService,
            ActivityLog,
            CompanionJournal);
        AchievementDeliveryService.AccountCloud = AccountCloud;
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
        SteamMonitorCoordinator.Library = CompanionLibrary;
        RelayCoordinator.Library = CompanionLibrary;
    }

    public AppPaths Paths { get; }

    public ActivityLog ActivityLog { get; }

    public AppUpdateService UpdateService { get; }

    public SecureWebhookProtector WebhookProtector { get; }

    public SettingsStore SettingsStore { get; }
    public AccountCloudClient AccountCloud { get; }
    public AccountSyncService AccountSync { get; }

    public EventLedger EventLedger { get; }
    public CompanionJournal CompanionJournal { get; }
    public CompanionLibrary CompanionLibrary { get; }

    public DiscordWebhookClient WebhookClient { get; }

    public AchievementArtworkClient ArtworkClient { get; }

    public AchievementOverlayService AchievementOverlayService { get; }

    public DiscordCollectorCardRenderer CollectorCardRenderer { get; }

    public DiscordAchievementPostComposer AchievementPostComposer { get; }

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
        UpdateService.Dispose();
        SteamMonitorCoordinator.Dispose();
        RelayCoordinator.Dispose();
        AchievementDeliveryService.Dispose();
        AchievementOverlayService.Dispose();
        ArtworkClient.Dispose();
        OpenXblClient.Dispose();
        SteamRarityClient.Dispose();
        WebhookClient.Dispose();
        AccountCloud.Dispose();
    }
}
