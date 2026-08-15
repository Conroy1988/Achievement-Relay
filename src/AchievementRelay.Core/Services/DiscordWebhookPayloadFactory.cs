using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class DiscordWebhookPayloadFactory
{
    private const int XboxGreen = 0x107C10;
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

        if (achievement.IsRare)
        {
            fields.Add(new { name = "Rarity", value = "💎 Rare achievement", inline = true });
        }

        if (!string.IsNullOrWhiteSpace(settings.DisplayName))
        {
            fields.Add(new { name = "Player", value = Truncate(settings.DisplayName, 1024), inline = true });
        }

        var description = settings.IncludeRawDetailsWhenUncertain
            ? achievement.Description
            : null;
        var unlockTimeEstimated = achievement.UnlockTimeEstimated || achievement.UnlockedAt is null;

        var embed = new Dictionary<string, object?>
        {
            ["title"] = Truncate($"🏆 {achievement.Name}", 256),
            ["color"] = achievement.IsRare ? RareGold : XboxGreen,
            ["timestamp"] = (achievement.UnlockedAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O"),
            ["footer"] = new
            {
                text = unlockTimeEstimated
                    ? "Relayed by Achievement Relay • detected time shown (Xbox supplied no unlock time)"
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

        if (Uri.TryCreate(achievement.ImageUrl, UriKind.Absolute, out var imageUri) &&
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
                    description = "This channel is ready. Your next detected Xbox achievement will appear here automatically.",
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
        var normalized = string.IsNullOrWhiteSpace(value) ? "Achievement Relay" : value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..(maximumLength - 1)] + "…";
    }
}
