using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class DiscordWebhookPayloadFactory
{
    private const int XboxGreen = 0x107C10;
    private const int SteamBlue = 0x1B6E9F;
    private const int Bronze = 0xCD7F32;
    private const int Silver = 0xC0C5C8;
    private const int Gold = 0xF2C94C;
    private const int Platinum = 0x72E2F1;
    private const string ProjectUrl = "https://github.com/Conroy1988/Achievement-Relay";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Create(AchievementEvent achievement, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        ArgumentNullException.ThrowIfNull(settings);

        var fields = new List<object>();

        if (!string.IsNullOrWhiteSpace(achievement.GameName))
        {
            fields.Add(new { name = "Game", value = Truncate(achievement.GameName, 1024), inline = true });
        }

        if (achievement.Gamerscore is not null)
        {
            fields.Add(new { name = "Gamerscore", value = $"+{achievement.Gamerscore}G", inline = true });
        }

        var rarityTier = RelayRarityClassifier.Classify(achievement.RarityPercentage);
        if (rarityTier != RelayRarityTier.Unranked)
        {
            var population = string.Equals(achievement.SourceProvider, "Steam", StringComparison.OrdinalIgnoreCase)
                ? "Steam players"
                : "players";
            fields.Add(new
            {
                name = "Rarity",
                value = $"{GetTierTextIcon(rarityTier)} Relay {RelayRarityClassifier.DisplayName(rarityTier)} tier • " +
                        $"{RelayRarityClassifier.FormatPercentage(achievement.RarityPercentage)} of {population}",
                inline = true
            });
        }
        else if (achievement.IsRare)
        {
            fields.Add(new { name = "Rarity", value = "◇ Rare achievement • global percentage unavailable", inline = true });
        }
        else
        {
            fields.Add(new { name = "Rarity", value = "◇ Unranked • global percentage unavailable", inline = true });
        }

        var playerName = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? achievement.PlayerName
            : settings.DisplayName;
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            fields.Add(new { name = "Player", value = Truncate(playerName, 1024), inline = true });
        }

        var platform = ResolvePlatform(achievement);
        if (!string.IsNullOrWhiteSpace(platform))
        {
            fields.Add(new { name = "Platform", value = Truncate(platform, 1024), inline = true });
        }

        fields.Add(CreateProjectLinkField());

        var description = settings.IncludeRawDetailsWhenUncertain
            ? achievement.Description
            : null;
        var unlockTimeEstimated = achievement.UnlockTimeEstimated || achievement.UnlockedAt is null;

        var embed = new Dictionary<string, object?>
        {
            ["title"] = Truncate($"🏆 {achievement.Name}", 256),
            ["color"] = GetEmbedColor(rarityTier, achievement.SourceProvider),
            ["timestamp"] = (achievement.UnlockedAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O"),
            ["footer"] = new
            {
                text = unlockTimeEstimated
                    ? "Relayed by Achievement Relay • detected time shown (the platform supplied no unlock time)"
                    : "Relayed by Achievement Relay"
            }
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            embed["description"] = Truncate(description, 4096);
        }

        if (fields.Count > 0)
        {
            embed["fields"] = fields;
        }

        var hasAttachment = achievement.ImageBytes is { Length: > 0 } &&
                            !string.IsNullOrWhiteSpace(achievement.ImageFileName);
        if (achievement.IsCollectorCard && hasAttachment)
        {
            embed["image"] = new { url = $"attachment://{achievement.ImageFileName}" };
        }
        else if (hasAttachment)
        {
            embed["thumbnail"] = new { url = $"attachment://{achievement.ImageFileName}" };
        }
        else if (Uri.TryCreate(achievement.ImageUrl, UriKind.Absolute, out var imageUri) &&
            (imageUri.Scheme == Uri.UriSchemeHttps || imageUri.Scheme == Uri.UriSchemeHttp))
        {
            embed["thumbnail"] = new { url = imageUri.ToString() };
        }

        var payload = new Dictionary<string, object?>
        {
            ["username"] = Truncate(settings.DiscordUsername, 80),
            ["allowed_mentions"] = new { parse = Array.Empty<string>() },
            ["embeds"] = new[] { embed }
        };

        if (achievement.IsCollectorCard && hasAttachment)
        {
            payload["attachments"] = new[]
            {
                new
                {
                    id = 0,
                    filename = achievement.ImageFileName,
                    description = CreateAttachmentDescription(achievement, settings, rarityTier, platform)
                }
            };
        }

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    public static string CreateConnectionTest(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var payload = new
        {
            username = Truncate(settings.DiscordUsername, 80),
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[]
            {
                new
                {
                    title = "✅ Achievement Relay connected",
                    description = "This channel is ready. Your next detected Xbox or Steam achievement will appear here automatically.",
                    color = XboxGreen,
                    fields = new[] { CreateProjectLinkField() },
                    footer = new { text = "Every achievement. Reliably shared." },
                    timestamp = DateTimeOffset.UtcNow.ToString("O")
                }
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static object CreateProjectLinkField() => new
    {
        name = "Achievement Relay",
        value = $"[Get the relay]({ProjectUrl})",
        inline = false
    };

    private static string ResolvePlatform(AchievementEvent achievement)
    {
        if (!string.IsNullOrWhiteSpace(achievement.Platform))
        {
            return achievement.Platform;
        }

        return string.Equals(achievement.SourceProvider, "OpenXBL", StringComparison.OrdinalIgnoreCase)
            ? "Xbox"
            : achievement.SourceProvider;
    }

    private static int GetEmbedColor(RelayRarityTier tier, string sourceProvider) => tier switch
    {
        RelayRarityTier.Bronze => Bronze,
        RelayRarityTier.Silver => Silver,
        RelayRarityTier.Gold => Gold,
        RelayRarityTier.Platinum => Platinum,
        _ => string.Equals(sourceProvider, "Steam", StringComparison.OrdinalIgnoreCase)
            ? SteamBlue
            : XboxGreen
    };

    private static string GetTierTextIcon(RelayRarityTier tier) => tier switch
    {
        RelayRarityTier.Bronze => "🥉",
        RelayRarityTier.Silver => "◈",
        RelayRarityTier.Gold => "🏅",
        RelayRarityTier.Platinum => "💠",
        _ => "◇"
    };

    private static string CreateAttachmentDescription(
        AchievementEvent achievement,
        AppSettings settings,
        RelayRarityTier tier,
        string platform)
    {
        var game = string.IsNullOrWhiteSpace(achievement.GameName)
            ? string.Empty
            : $" in {achievement.GameName.Trim()}";
        var rarity = tier == RelayRarityTier.Unranked
            ? "Global rarity percentage unavailable."
            : $"Relay {RelayRarityClassifier.DisplayName(tier)} tier; " +
              $"unlocked by {RelayRarityClassifier.FormatPercentage(achievement.RarityPercentage)} of players.";
        var player = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? achievement.PlayerName
            : settings.DisplayName;
        var playerText = string.IsNullOrWhiteSpace(player)
            ? string.Empty
            : $" Player: {player.Trim()}.";
        var platformText = string.IsNullOrWhiteSpace(platform)
            ? string.Empty
            : $" Platform: {platform.Trim()}.";
        return Truncate(
            $"Achievement unlocked: {achievement.Name}{game}. {rarity}{playerText}{platformText}",
            1024);
    }

    private static string Truncate(string? value, int maximumLength)
    {
        var source = string.IsNullOrWhiteSpace(value) ? "Achievement Relay" : value.Trim();
        var normalized = NormalizeUnicode(source);
        if (normalized.Length <= maximumLength)
        {
            return normalized;
        }

        var contentLength = maximumLength - 1;
        if (contentLength > 0 &&
            char.IsHighSurrogate(normalized[contentLength - 1]) &&
            char.IsLowSurrogate(normalized[contentLength]))
        {
            contentLength--;
        }

        return normalized[..contentLength] + "…";
    }

    private static string NormalizeUnicode(string value)
    {
        var normalized = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (char.IsHighSurrogate(current) &&
                index + 1 < value.Length &&
                char.IsLowSurrogate(value[index + 1]))
            {
                normalized.Append(current);
                normalized.Append(value[++index]);
            }
            else if (char.IsSurrogate(current))
            {
                // Steam and other providers can expose malformed UTF-16 in
                // localized metadata. Preserve the payload with a standard
                // replacement character instead of failing JSON serialization.
                normalized.Append('\uFFFD');
            }
            else
            {
                normalized.Append(current);
            }
        }

        return normalized.ToString();
    }
}
