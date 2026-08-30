using System.Globalization;
using System.Text;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed record AchievementOverlayPresentation(
    string AchievementName,
    string GameAndReward,
    string Platform,
    string Eyebrow,
    string Percentage,
    string TierName,
    string TierDescription,
    RelayRarityTier Tier,
    byte[]? AchievementIconBytes,
    string AccessibleAnnouncement)
{
    private const int MaximumAchievementNameLength = 72;
    private const int MaximumGameNameLength = 52;
    private const int MaximumPlatformLength = 28;

    public static AchievementOverlayPresentation Create(
        AchievementEvent achievement,
        byte[]? achievementIconBytes = null)
    {
        ArgumentNullException.ThrowIfNull(achievement);

        var achievementName = NormalizeText(
            achievement.Name,
            MaximumAchievementNameLength,
            "Achievement unlocked");
        var gameName = NormalizeText(
            achievement.GameName,
            MaximumGameNameLength,
            NormalizeText(achievement.SourceProvider, MaximumGameNameLength, "Achievement Relay"));
        var platform = NormalizeText(
            achievement.Platform,
            MaximumPlatformLength,
            NormalizeText(achievement.SourceProvider, MaximumPlatformLength, "Achievement Relay"));
        var tier = RelayRarityClassifier.Classify(achievement.RarityPercentage);
        var tierName = RelayRarityClassifier.DisplayName(tier);
        var percentage = RelayRarityClassifier.FormatPercentage(achievement.RarityPercentage);
        var gamerscore = achievement.Gamerscore is { } suppliedGamerscore && suppliedGamerscore > 0
            ? suppliedGamerscore
            : (int?)null;
        var reward = gamerscore is { } rewardGamerscore
            ? $" · +{rewardGamerscore}G"
            : string.Empty;
        var gameAndReward = string.Concat(gameName, reward);
        var eyebrow = string.Concat("ACHIEVEMENT UNLOCKED · ", platform.ToUpperInvariant());
        var rarityAnnouncement = tier == RelayRarityTier.Unranked
            ? "Global rarity unavailable. Relay Unranked tier"
            : string.Concat(percentage, " of players. Relay ", tierName, " tier");
        var announcement = string.Concat(
            "Achievement unlocked: ",
            achievementName,
            " in ",
            gameName,
            ". Platform: ",
            platform,
            ". ",
            rarityAnnouncement,
            gamerscore is { } announcementGamerscore ? $", {announcementGamerscore} Gamerscore" : string.Empty,
            ".");

        return new AchievementOverlayPresentation(
            achievementName,
            gameAndReward,
            platform,
            eyebrow,
            percentage,
            tierName,
            RelayRarityClassifier.Description(tier),
            tier,
            CopyBoundedIcon(achievementIconBytes ?? achievement.ImageBytes),
            announcement);
    }

    private static byte[]? CopyBoundedIcon(byte[]? bytes)
    {
        const int maximumIconBytes = 6 * 1024 * 1024;
        return bytes is { Length: > 0 and <= maximumIconBytes }
            ? bytes.ToArray()
            : null;
    }

    private static string NormalizeText(string? value, int maximumLength, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var builder = new StringBuilder(Math.Min(value.Length, maximumLength));
        var lastWasSpace = false;
        foreach (var rune in value.EnumerateRunes())
        {
            var isSpace = Rune.IsWhiteSpace(rune) || Rune.IsControl(rune);
            if (isSpace)
            {
                if (!lastWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            // Provider-controlled display names must not be able to visually
            // reorder or conceal the trusted rarity/platform facts around
            // them with bidi overrides, isolates, or zero-width formatting.
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(rune.ToString());
            lastWasSpace = false;
            if (builder.Length >= maximumLength)
            {
                break;
            }
        }

        var normalized = builder.ToString().Trim();
        if (normalized.Length == 0)
        {
            return fallback;
        }

        if (normalized.Length < maximumLength && value.Length <= maximumLength)
        {
            return normalized;
        }

        var contentLength = Math.Min(normalized.Length, maximumLength - 1);
        if (contentLength > 0 &&
            contentLength < normalized.Length &&
            char.IsHighSurrogate(normalized[contentLength - 1]) &&
            char.IsLowSurrogate(normalized[contentLength]))
        {
            contentLength--;
        }

        return string.Concat(normalized[..contentLength], "…");
    }
}
