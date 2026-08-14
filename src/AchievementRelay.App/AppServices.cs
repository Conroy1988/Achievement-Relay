using AchievementRelay.App.Services;
using AchievementRelay.Core.Services;

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
        Classifier = new XboxNotificationClassifier();
        Parser = new AchievementNotificationParser(Classifier);
        NotificationListener = new XboxNotificationListenerService(Classifier, ActivityLog);
        StartupService = new StartupService(ActivityLog);
        RelayCoordinator = new RelayCoordinator(
            NotificationListener,
            Parser,
            SettingsStore,
            WebhookProtector,
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

    public XboxNotificationClassifier Classifier { get; }

    public AchievementNotificationParser Parser { get; }

    public XboxNotificationListenerService NotificationListener { get; }

    public StartupService StartupService { get; }

    public RelayCoordinator RelayCoordinator { get; }

    public void Dispose()
    {
        RelayCoordinator.Dispose();
        NotificationListener.Dispose();
        WebhookClient.Dispose();
    }
}
