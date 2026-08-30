using System.Runtime.InteropServices;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed record DiscordAchievementPost(
    string JsonPayload,
    byte[]? AttachmentBytes,
    string? AttachmentFileName,
    string? AttachmentContentType,
    bool UsesCollectorCard);

/// <summary>
/// Builds the visual Discord post without making presentation enrichment a
/// delivery dependency. If card composition ever fails, the exact pre-card
/// embed and provider thumbnail behavior remains available immediately.
/// </summary>
public sealed class DiscordAchievementPostComposer(
    AchievementArtworkClient artworkClient,
    DiscordCollectorCardRenderer cardRenderer,
    ActivityLog activityLog)
{
    public async Task<DiscordAchievementPost> ComposeAsync(
        AchievementEvent achievement,
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        ArgumentNullException.ThrowIfNull(settings);

        try
        {
            var artwork = await artworkClient.GetAsync(achievement, cancellationToken);
            var card = cardRenderer.Render(achievement, settings, artwork);
            var cardAchievement = achievement with
            {
                ImageBytes = card.Bytes,
                ImageFileName = card.FileName,
                ImageContentType = card.ContentType,
                IsCollectorCard = true
            };
            return new DiscordAchievementPost(
                DiscordWebhookPayloadFactory.Create(cardAchievement, settings),
                card.Bytes,
                card.FileName,
                card.ContentType,
                UsesCollectorCard: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRecoverablePresentationFailure(exception))
        {
            activityLog.Warning(
                $"Collector Card rendering was unavailable for {achievement.Name}; the standard Discord presentation was used safely.");
            return CreateLegacyPost(achievement, settings);
        }
    }

    private static DiscordAchievementPost CreateLegacyPost(
        AchievementEvent achievement,
        AppSettings settings)
    {
        var legacyAchievement = achievement.IsCollectorCard
            ? achievement with
            {
                ImageBytes = null,
                ImageFileName = null,
                ImageContentType = null,
                IsCollectorCard = false
            }
            : achievement;
        return new DiscordAchievementPost(
            DiscordWebhookPayloadFactory.Create(legacyAchievement, settings),
            legacyAchievement.ImageBytes,
            legacyAchievement.ImageFileName,
            legacyAchievement.ImageContentType,
            UsesCollectorCard: false);
    }

    private static bool IsRecoverablePresentationFailure(Exception exception) =>
        exception is ArgumentException or
            ExternalException or
            IOException or
            InvalidDataException or
            InvalidOperationException or
            NotSupportedException or
            PlatformNotSupportedException;
}
