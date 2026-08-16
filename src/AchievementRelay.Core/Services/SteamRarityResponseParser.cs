using System.Globalization;
using System.Text.Json;

namespace AchievementRelay.Core.Services;

public static class SteamRarityResponseParser
{
    private const int MaximumApiNameLength = 512;

    public static IReadOnlyDictionary<string, double> Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    public static IReadOnlyDictionary<string, double> Parse(JsonElement documentRoot)
    {
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        if (documentRoot.ValueKind != JsonValueKind.Object ||
            !documentRoot.TryGetProperty("achievementpercentages", out var root) ||
            root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("achievements", out var achievements) ||
            achievements.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in achievements.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("name", out var nameValue) ||
                nameValue.ValueKind != JsonValueKind.String ||
                nameValue.GetString()?.Trim() is not { Length: > 0 } name ||
                name.Length > MaximumApiNameLength ||
                !item.TryGetProperty("percent", out var percentValue) ||
                !TryReadPercentage(percentValue, out var percentage))
            {
                continue;
            }

            result[name] = percentage;
        }

        return result;
    }

    private static bool TryReadPercentage(JsonElement value, out double percentage)
    {
        percentage = 0;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (!value.TryGetDouble(out percentage))
            {
                return false;
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            if (!double.TryParse(
                    value.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out percentage))
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        return double.IsFinite(percentage) && percentage is >= 0 and <= 100;
    }
}
