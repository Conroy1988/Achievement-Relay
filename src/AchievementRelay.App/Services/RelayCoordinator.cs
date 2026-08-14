using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed class RelayCoordinator(
    XboxNotificationListenerService notificationListener,
    AchievementNotificationParser parser,
    SettingsStore settingsStore,
    SecureWebhookProtector webhookProtector,
    EventLedger eventLedger,
    DiscordWebhookClient webhookClient,
    ActivityLog activityLog) : IDisposable
{
    private readonly SemaphoreSlim _relayGate = new(1, 1);
    private bool _started;

    public bool IsRunning => _started;

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return true;
        }

        notificationListener.XboxNotificationCaptured += OnXboxNotificationCaptured;
        _started = await notificationListener.StartAsync(cancellationToken);
        if (!_started)
        {
            notificationListener.XboxNotificationCaptured -= OnXboxNotificationCaptured;
        }

        return _started;
    }

    public async Task<int> RescanAsync(CancellationToken cancellationToken = default) =>
        await notificationListener.RescanCurrentXboxNotificationsAsync(cancellationToken);

    public void Dispose()
    {
        notificationListener.XboxNotificationCaptured -= OnXboxNotificationCaptured;
        _relayGate.Dispose();
    }

    private async void OnXboxNotificationCaptured(object? sender, RawNotificationCapturedEventArgs args)
    {
        try
        {
            await ProcessAsync(args.Notification, CancellationToken.None);
        }
        catch (Exception exception)
        {
            activityLog.Error($"Unexpected relay error: {exception.Message}");
        }
    }

    private async Task ProcessAsync(RawNotification notification, CancellationToken cancellationToken)
    {
        await _relayGate.WaitAsync(cancellationToken);
        try
        {
            var achievement = parser.Parse(notification);
            if (achievement is null)
            {
                activityLog.Info($"Ignored a non-achievement Xbox notification from {notification.ApplicationDisplayName}.");
                return;
            }

            if (await eventLedger.ContainsAsync(achievement.Id, cancellationToken))
            {
                activityLog.Info($"Skipped duplicate achievement: {achievement.Name}.");
                return;
            }

            var settings = await settingsStore.LoadAsync(cancellationToken);
            if (settings.PostRareOnly && !achievement.IsRare)
            {
                await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
                activityLog.Info($"Skipped common achievement because Rare Only is enabled: {achievement.Name}.");
                return;
            }

            var webhookValue = webhookProtector.TryUnprotect(settings.ProtectedWebhookUrl);
            if (!WebhookUrlValidator.TryNormalize(webhookValue, out var webhookUri, out _) || webhookUri is null)
            {
                activityLog.Warning($"Captured {achievement.Name}, but Discord is not configured yet.");
                return;
            }

            activityLog.Info($"Xbox achievement detected: {achievement.Name}.");
            var payload = DiscordWebhookPayloadFactory.Create(achievement, settings);
            var result = await SendWithRetryAsync(webhookUri, payload, cancellationToken);

            if (!result.Success)
            {
                activityLog.Error($"Could not relay {achievement.Name}: {result.Message}");
                return;
            }

            await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
            activityLog.Success($"Posted {achievement.Name} to Discord.");
        }
        finally
        {
            _relayGate.Release();
        }
    }

    private async Task<RelayResult> SendWithRetryAsync(
        Uri webhookUri,
        string payload,
        CancellationToken cancellationToken)
    {
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12) };
        RelayResult result = RelayResult.Fail("Delivery did not start.");

        foreach (var delay in delays)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            result = await webhookClient.SendAsync(webhookUri, payload, cancellationToken);
            if (result.Success || result.StatusCode is >= 400 and < 500 and not 429)
            {
                break;
            }
        }

        return result;
    }
}
