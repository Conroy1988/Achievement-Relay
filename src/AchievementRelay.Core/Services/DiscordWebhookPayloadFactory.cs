using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class DiscordWebhookPayloadFactory
{
    private const int XboxGreen = 0x107C10;
    private const int SteamBlue = 0x1B6E9F;
    private const int RareGold = 0xF2C94C;

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

        if (achievement.RarityPercentage is { } rarityPercentage)
        {
            fields.Add(new
            {
                name = "Rarity",
                value = achievement.IsRare
                    ? $"💎 Rare achievement • {rarityPercentage:0.##}% of players"
                    : $"{rarityPercentage:0.##}% of players",
                inline = true
            });
        }
        else if (achievement.IsRare)
        {
            fields.Add(new { name = "Rarity", value = "💎 Rare achievement", inline = true });
        }

        var playerName = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? achievement.PlayerName
            : settings.DisplayName;
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            fields.Add(new { name = "Player", value = Truncate(playerName, 1024), inline = true });
        }

        if (!string.IsNullOrWhiteSpace(achievement.SourceProvider))
        {
            var platform = string.Equals(achievement.SourceProvider, "OpenXBL", StringComparison.OrdinalIgnoreCase)
                ? "Xbox"
                : achievement.SourceProvider;
            fields.Add(new { name = "Platform", value = Truncate(platform, 1024), inline = true });
        }

        var description = settings.IncludeRawDetailsWhenUncertain
            ? achievement.Description
            : null;
        var unlockTimeEstimated = achievement.UnlockTimeEstimated || achievement.UnlockedAt is null;

        var embed = new Dictionary<string, object?>
        {
            ["title"] = Truncate($"🏆 {achievement.Name}", 256),
            ["color"] = achievement.IsRare
                ? RareGold
                : string.Equals(achievement.SourceProvider, "Steam", StringComparison.OrdinalIgnoreCase)
                    ? SteamBlue
                    : XboxGreen,
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

        if (achievement.ImageBytes is { Length: > 0 } && !string.IsNullOrWhiteSpace(achievement.ImageFileName))
        {
            embed["thumbnail"] = new { url = $"attachment://{achievement.ImageFileName}" };
        }
        else if (Uri.TryCreate(achievement.ImageUrl, UriKind.Absolute, out var imageUri) &&
            (imageUri.Scheme == Uri.UriSchemeHttps || imageUri.Scheme == Uri.UriSchemeHttp))
        {
            embed["thumbnail"] = new { url = imageUri.ToString() };
        }

        var payload = new
        {
            username = Truncate(settings.DiscordUsername, 80),
            allowed_mentions = new { parse = Array.Empty<string>() },
            embeds = new[] { embed }
        };

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
                    footer = new { text = "Every achievement. Reliably shared." },
                    timestamp = DateTimeOffset.UtcNow.ToString("O")
                }
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
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
