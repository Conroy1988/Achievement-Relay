using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;
using System.IO;

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
    ActivityLog activityLog,
    CompanionJournal? journal = null) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    // Observational only: failures in the desktop showcase must never retry a post.
    public event Action<AchievementEvent, DiscordAchievementPost>? AchievementPosted;

    public async Task<AchievementDeliveryResult> DeliverAsync(
        AchievementEvent achievement,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken);
        SharedDeliveryClaim? shared = null;
        try
        {
            if (await eventLedger.ContainsAsync(achievement.Id, cancellationToken))
            {
                return AchievementDeliveryResult.Handled;
            }

            settings = CompanionPolicy.ForGame(achievement, settings);
            if (journal is not null) await journal.RecordAsync(achievement, "Pending");

            if (settings.PostRareOnly && achievement.RarityKnown && !achievement.IsRare)
            {
                await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
                TryQueueOverlay(achievement, settings, achievement.ImageBytes);
                activityLog.Info($"Skipped common {achievement.SourceProvider} achievement because Rare Only is enabled: {achievement.Name}.");
                if (journal is not null) await journal.RecordAsync(achievement, "Filtered");
                return AchievementDeliveryResult.Handled;
            }

            var webhookValue = secretProtector.TryUnprotect(settings.ProtectedWebhookUrl);
            if (!WebhookUrlValidator.TryNormalize(webhookValue, out var webhookUri, out _) || webhookUri is null)
            {
                activityLog.Warning($"Found {achievement.Name}, but Discord is not configured.");
                if (journal is not null) await journal.RecordAsync(achievement, "Needs connection");
                return AchievementDeliveryResult.RetryRequired;
            }

            activityLog.Info($"{achievement.SourceProvider} achievement detected: {achievement.Name}.");
            if (!string.IsNullOrWhiteSpace(settings.Companion.SharedDeliveryFolder))
            {
                try { shared = SharedDeliveryClaim.Acquire(settings.Companion.SharedDeliveryFolder, achievement.Id, webhookUri); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (journal is not null) await journal.RecordAsync(achievement, "Shared delivery unavailable");
                    activityLog.Warning("Shared delivery is busy or unavailable. No uncoordinated post was sent.");
                    return AchievementDeliveryResult.RetryRequired;
                }
                if (shared.State == "delivered")
                {
                    await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
                    if (journal is not null) await journal.RecordAsync(achievement, "Delivered on another PC");
                    return AchievementDeliveryResult.Handled;
                }
                if (shared.State == "sending")
                {
                    if (journal is not null) await journal.RecordAsync(achievement, "Delivery uncertain — check Discord");
                    return AchievementDeliveryResult.RetryRequired;
                }
            }
            var post = await postComposer.ComposeAsync(achievement, settings, cancellationToken);
            shared?.SetState("sending");
            var result = shared is null ? await SendWithRetryAsync(webhookUri, post, cancellationToken) :
                await webhookClient.SendAsync(webhookUri, post.JsonPayload, post.AttachmentBytes,
                    post.AttachmentFileName, post.AttachmentContentType, cancellationToken);
            if (!result.Success)
            {
                if (result.StatusCode is >= 400 and < 500) shared?.SetState("pending");
                activityLog.Error($"Could not relay {achievement.Name}: {result.Message}");
                if (journal is not null) await journal.RecordAsync(achievement,
                    shared?.State == "sending" ? "Delivery uncertain — check Discord" : "Retry pending");
                return AchievementDeliveryResult.RetryRequired;
            }

            shared?.SetState("delivered");

            await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
            TryQueueOverlay(achievement, settings, post.AchievementIconBytes);
            activityLog.Success($"Posted {achievement.Name} from {achievement.SourceProvider} to Discord.");
            if (journal is not null) await journal.RecordAsync(achievement, "Delivered", post.AchievementIconBytes);
            foreach (var observer in AchievementPosted?.GetInvocationList() ?? [])
            {
                try { ((Action<AchievementEvent, DiscordAchievementPost>)observer)(achievement, post); }
                catch (Exception) { /* The delivery has already completed durably. */ }
            }
            return AchievementDeliveryResult.Posted;
        }
        finally
        {
            try { shared?.Dispose(); }
            finally { _gate.Release(); }
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
