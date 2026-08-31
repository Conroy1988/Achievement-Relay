using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public enum AchievementDeliveryResult
{
    Handled,
    Posted,
    RetryRequired
}

public sealed class AchievementDeliveryService(
    SecureWebhookProtector secretProtector,
    EventLedger eventLedger,
    DiscordWebhookClient webhookClient,
    DiscordAchievementPostComposer postComposer,
    AchievementOverlayService overlayService,
    ActivityLog activityLog) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AchievementDeliveryResult> DeliverAsync(
        AchievementEvent achievement,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (await eventLedger.ContainsAsync(achievement.Id, cancellationToken))
            {
                return AchievementDeliveryResult.Handled;
            }

            if (settings.PostRareOnly && achievement.RarityKnown && !achievement.IsRare)
            {
                await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
                TryQueueOverlay(achievement, settings, achievement.ImageBytes);
                activityLog.Info($"Skipped common {achievement.SourceProvider} achievement because Rare Only is enabled: {achievement.Name}.");
                return AchievementDeliveryResult.Handled;
            }

            var webhookValue = secretProtector.TryUnprotect(settings.ProtectedWebhookUrl);
            if (!WebhookUrlValidator.TryNormalize(webhookValue, out var webhookUri, out _) || webhookUri is null)
            {
                activityLog.Warning($"Found {achievement.Name}, but Discord is not configured.");
                return AchievementDeliveryResult.RetryRequired;
            }

            activityLog.Info($"{achievement.SourceProvider} achievement detected: {achievement.Name}.");
            var post = await postComposer.ComposeAsync(achievement, settings, cancellationToken);
            var result = await SendWithRetryAsync(webhookUri, post, cancellationToken);
            if (!result.Success)
            {
                activityLog.Error($"Could not relay {achievement.Name}: {result.Message}");
                return AchievementDeliveryResult.RetryRequired;
            }

            await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
            TryQueueOverlay(achievement, settings, post.AchievementIconBytes);
            activityLog.Success($"Posted {achievement.Name} from {achievement.SourceProvider} to Discord.");
            return AchievementDeliveryResult.Posted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private void TryQueueOverlay(
        AchievementEvent achievement,
        AppSettings settings,
        byte[]? achievementIconBytes)
    {
        try
        {
            overlayService.Enqueue(achievement, settings, achievementIconBytes);
        }
        catch (Exception)
        {
            activityLog.Warning(
                $"The Signal Strip could not queue {achievement.Name}; Discord delivery was not affected.");
        }
    }

    private async Task<RelayResult> SendWithRetryAsync(
        Uri webhookUri,
        DiscordAchievementPost post,
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

            result = await webhookClient.SendAsync(
                webhookUri,
                post.JsonPayload,
                post.AttachmentBytes,
                post.AttachmentFileName,
                post.AttachmentContentType,
                cancellationToken);
            if (result.Success || result.StatusCode is >= 400 and < 500 and not 429)
            {
                break;
            }
        }

        return result;
    }
}
