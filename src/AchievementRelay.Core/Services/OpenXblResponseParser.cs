using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class OpenXblResponseParser
{
    public static XboxAccount ParseAccount(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json.Trim().TrimStart('\uFEFF'));
        if (TryParseAccountElement(document.RootElement, 0, string.Empty, string.Empty, out var account))
        {
            return account;
        }

        throw new JsonException(
            "OpenXBL accepted the API key, but did not return a usable Xbox profile. " +
            "Confirm the intended Xbox profile is connected in OpenXBL, then try again.");
    }

    private static bool TryParseAccountElement(
        JsonElement element,
        int depth,
        string inheritedXuid,
        string inheritedGamertag,
        out XboxAccount account)
    {
        account = default!;
        if (depth > 8)
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.String && depth < 2)
        {
            var nestedJson = element.GetString();
            if (!string.IsNullOrWhiteSpace(nestedJson))
            {
                try
                {
                    using var nestedDocument = JsonDocument.Parse(nestedJson.Trim().TrimStart('\uFEFF'));
                    return TryParseAccountElement(
                        nestedDocument.RootElement,
                        depth + 1,
                        inheritedXuid,
                        inheritedGamertag,
                        out account);
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            return false;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (TryParseAccountElement(
                        item,
                        depth + 1,
                        inheritedXuid,
                        inheritedGamertag,
                        out account))
                {
                    return true;
                }
            }

            return false;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var xuid = FirstNonEmpty(
            GetAccountXuid(element),
            inheritedXuid);
        var directGamertag = GetString(
            element,
            "gamertag",
            "uniqueModernGamertag",
            "modernGamertag",
            "gameDisplayName",
            "displayName");
        var gamertag = FirstNonEmpty(directGamertag, inheritedGamertag);

        if (TryGetProperty(element, "settings", out var settings))
        {
            gamertag = FirstNonEmpty(
                GetAccountSetting(settings, "Gamertag"),
                GetAccountSetting(settings, "UniqueModernGamertag"),
                GetAccountSetting(settings, "ModernGamertag"),
                GetAccountSetting(settings, "GameDisplayName"),
                directGamertag,
                inheritedGamertag);
        }

        if (!string.IsNullOrWhiteSpace(xuid) && !string.IsNullOrWhiteSpace(gamertag))
        {
            account = new XboxAccount(xuid.Trim(), gamertag.Trim());
            return true;
        }

        foreach (var containerName in new[]
                 {
                     "profileUsers", "people", "profiles", "users", "accounts", "items", "data", "result", "response",
                     "payload", "value", "body", "content", "account", "profile"
                 })
        {
            if (TryGetProperty(element, containerName, out var container) &&
                TryParseAccountElement(container, depth + 1, xuid, gamertag, out account))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetAccountXuid(JsonElement element)
    {
        foreach (var propertyName in new[] { "xuid", "xboxUserId", "userId", "id", "hostId" })
        {
            var value = GetString(element, propertyName).Trim();
            if (value.Length is >= 12 and <= 20 && value.All(char.IsAsciiDigit))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string GetAccountSetting(JsonElement settings, string settingName)
    {
        if (settings.ValueKind == JsonValueKind.Object)
        {
            var directValue = GetString(settings, settingName);
            if (!string.IsNullOrWhiteSpace(directValue))
            {
                return directValue;
            }

            if (TryGetProperty(settings, "values", out var values))
            {
                return GetAccountSetting(values, settingName);
            }
        }

        if (settings.ValueKind == JsonValueKind.Array)
        {
            foreach (var setting in settings.EnumerateArray())
            {
                if (GetString(setting, "id", "name", "key").Equals(settingName, StringComparison.OrdinalIgnoreCase))
                {
                    return GetString(setting, "value");
                }
            }
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    public static IReadOnlyList<XboxTitleProgress> ParseTitleProgress(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        var root = ParseJsonRoot(json);
        var titles = GetArray(root, "titles", "userTitles", "items");
        if (titles is null)
        {
            throw new JsonException("OpenXBL did not return an Xbox title collection.");
        }

        var parsed = new List<XboxTitleProgress>();
        foreach (var item in titles.Value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var titleId = GetString(item, "titleId", "id");
            if (string.IsNullOrWhiteSpace(titleId))
            {
                continue;
            }

            var achievement = TryGetProperty(item, "achievement", out var achievementValue) &&
                              achievementValue.ValueKind == JsonValueKind.Object
                ? achievementValue
                : item;
            var titleHistory = TryGetProperty(item, "titleHistory", out var historyValue) &&
                               historyValue.ValueKind == JsonValueKind.Object
                ? historyValue
                : default;
            DateTimeOffset? lastPlayedAt = null;
            var lastPlayedValue = titleHistory.ValueKind == JsonValueKind.Object
                ? GetString(titleHistory, "lastTimePlayed", "lastPlayed")
                : GetString(item, "lastTimePlayed", "lastPlayed");
            if (DateTimeOffset.TryParse(
                    lastPlayedValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedLastPlayed))
            {
                lastPlayedAt = parsedLastPlayed;
            }

            var devices = new List<string>();
            if (TryGetProperty(item, "devices", out var deviceValues) &&
                deviceValues.ValueKind == JsonValueKind.Array)
            {
                foreach (var device in deviceValues.EnumerateArray())
                {
                    if (device.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(device.GetString()))
                    {
                        devices.Add(device.GetString()!.Trim());
                    }
                }
            }

            parsed.Add(new XboxTitleProgress
            {
                TitleId = titleId.Trim(),
                Name = NullIfWhiteSpace(GetString(item, "name", "titleName")),
                CurrentAchievements = GetNonNegativeInteger(achievement, "currentAchievements"),
                CurrentGamerscore = GetNonNegativeInteger(achievement, "currentGamerscore"),
                LastPlayedAt = lastPlayedAt,
                Devices = devices.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            });
        }

        return parsed
            .GroupBy(item => item.TitleId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.LastPlayedAt).First())
            .OrderByDescending(item => item.LastPlayedAt)
            .ThenBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AchievementEvent> ParseAchievements(
        string json,
        string accountId,
        string? fallbackTitleId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var root = ParseJsonRoot(json);
        var achievements = GetArray(root, "achievements", "items");
        if (achievements is null)
        {
            throw new JsonException("OpenXBL did not return an achievement collection.");
        }

        var parsed = new List<AchievementEvent>();
        foreach (var item in achievements.Value.EnumerateArray())
        {
            var achievement = ParseAchievement(item, accountId, fallbackTitleId);
            if (achievement is not null)
            {
                parsed.Add(achievement);
            }
        }

        return parsed
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.UnlockedAt)
            .ToArray();
    }

    private static AchievementEvent? ParseAchievement(
        JsonElement item,
        string accountId,
        string? fallbackTitleId)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !IsAchieved(item) ||
            GetBoolean(item, "isRevoked"))
        {
            return null;
        }

        var progression = TryGetProperty(item, "progression", out var value) && value.ValueKind == JsonValueKind.Object
            ? value
            : default;
        var unlockedValue = progression.ValueKind == JsonValueKind.Object
            ? GetString(progression, "timeUnlocked")
            : string.Empty;

        if (string.IsNullOrWhiteSpace(unlockedValue))
        {
            unlockedValue = GetString(item, "timeUnlocked", "unlockedAt", "dateUnlocked");
        }

        if (!DateTimeOffset.TryParse(
                unlockedValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var unlockedAt))
        {
            return null;
        }

        var achievementId = GetString(item, "id");
        var serviceConfigId = GetString(item, "serviceConfigId", "scid");
        var name = GetString(item, "name");
        if (string.IsNullOrWhiteSpace(achievementId) || string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var titleId = string.Empty;
        var gameName = string.Empty;
        if (TryGetProperty(item, "titleAssociations", out var associations) &&
            associations.ValueKind == JsonValueKind.Array &&
            associations.GetArrayLength() > 0)
        {
            var title = associations[0];
            titleId = GetString(title, "id", "titleId");
            gameName = GetString(title, "name");
        }

        if (string.IsNullOrWhiteSpace(gameName))
        {
            gameName = GetString(item, "gameName", "titleName");
        }

        if (string.IsNullOrWhiteSpace(titleId))
        {
            titleId = GetString(item, "titleId");
        }

        if (string.IsNullOrWhiteSpace(titleId))
        {
            titleId = fallbackTitleId ?? string.Empty;
        }

        return new AchievementEvent
        {
            Id = CreateIdentity(accountId, serviceConfigId, titleId, achievementId),
            Name = name,
            Description = NullIfWhiteSpace(GetString(item, "unlockedDescription", "description")),
            GameName = NullIfWhiteSpace(gameName),
            Gamerscore = GetGamerscore(item),
            IsRare = GetRarity(item),
            ImageUrl = NullIfWhiteSpace(GetImageUrl(item)),
            SourceProvider = "OpenXBL",
            UnlockedAt = unlockedAt
        };
    }

    private static JsonElement? GetArray(JsonElement root, params string[] propertyNames) =>
        GetArray(root, 0, propertyNames);

    private static JsonElement? GetArray(JsonElement root, int depth, string[] propertyNames)
    {
        if (depth > 5)
        {
            return null;
        }

        if (root.ValueKind == JsonValueKind.String)
        {
            var nestedJson = root.GetString();
            if (!string.IsNullOrWhiteSpace(nestedJson))
            {
                try
                {
                    using var nestedDocument = JsonDocument.Parse(nestedJson.Trim().TrimStart('\uFEFF'));
                    return GetArray(nestedDocument.RootElement.Clone(), depth + 1, propertyNames);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return null;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            return root;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var propertyName in propertyNames)
            {
                if (TryGetProperty(root, propertyName, out var values) &&
                    values.ValueKind == JsonValueKind.Array)
                {
                    return values;
                }
            }

            foreach (var containerName in new[]
                     {
                         "data", "result", "response", "payload", "value", "body", "content", "titleHistory", "history"
                     })
            {
                if (!TryGetProperty(root, containerName, out var container))
                {
                    continue;
                }

                var nested = GetArray(container, depth + 1, propertyNames);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static JsonElement ParseJsonRoot(string json)
    {
        using var document = JsonDocument.Parse(json.Trim().TrimStart('\uFEFF'));
        return document.RootElement.Clone();
    }

    private static bool IsAchieved(JsonElement item)
    {
        if (TryGetProperty(item, "unlocked", out var unlocked) &&
            unlocked.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return unlocked.GetBoolean();
        }

        var state = GetString(item, "progressState", "achievementState");
        if (state.Equals("Achieved", StringComparison.OrdinalIgnoreCase) || state == "1")
        {
            return true;
        }

        return TryGetProperty(item, "progression", out var progression) &&
               progression.ValueKind == JsonValueKind.Object &&
               GetString(progression, "achievementState").Equals("Achieved", StringComparison.OrdinalIgnoreCase);
    }

    private static int? GetGamerscore(JsonElement item)
    {
        if (TryGetProperty(item, "rewards", out var rewards) && rewards.ValueKind == JsonValueKind.Array)
        {
            foreach (var reward in rewards.EnumerateArray())
            {
                if (GetString(reward, "type").Equals("Gamerscore", StringComparison.OrdinalIgnoreCase) &&
                    TryGetInteger(reward, "value", out var score))
                {
                    return score;
                }
            }
        }

        return TryGetInteger(item, "gamerscore", out var directScore) ? directScore : null;
    }

    private static int GetNonNegativeInteger(JsonElement element, string propertyName) =>
        TryGetInteger(element, propertyName, out var value) ? Math.Max(0, value) : 0;

    private static bool GetRarity(JsonElement item)
    {
        if (TryGetProperty(item, "isRare", out var isRare) &&
            isRare.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return isRare.GetBoolean();
        }

        if (!TryGetProperty(item, "rarity", out var rarity))
        {
            return TryGetPercentage(item, "rarityPercentage", out var directPercentage) && directPercentage < 10;
        }

        if (rarity.ValueKind == JsonValueKind.Object)
        {
            var category = GetString(rarity, "currentCategory", "category");
            if (category.Equals("Rare", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return TryGetPercentage(rarity, "currentPercentage", out var percentage) && percentage < 10;
        }

        return rarity.ValueKind == JsonValueKind.Number && rarity.TryGetDouble(out var numeric) && numeric < 10;
    }

    private static string GetImageUrl(JsonElement item)
    {
        if (TryGetProperty(item, "mediaAssets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            var fallback = string.Empty;
            foreach (var asset in assets.EnumerateArray())
            {
                var url = GetString(asset, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    continue;
                }

                fallback = string.IsNullOrWhiteSpace(fallback) ? url : fallback;
                if (GetString(asset, "type").Equals("Icon", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }
        }

        return GetString(item, "imageUrl", "image");
    }

    private static string CreateIdentity(
        string accountId,
        string serviceConfigId,
        string titleId,
        string achievementId)
    {
        var canonical = string.Join('|',
            "openxbl-v1",
            accountId.Trim(),
            serviceConfigId.Trim(),
            titleId.Trim(),
            achievementId.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool GetBoolean(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        value.GetBoolean();

    private static bool TryGetInteger(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out value),
            JsonValueKind.String => int.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static bool TryGetPercentage(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static string GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }

            if (value.ValueKind == JsonValueKind.Number)
            {
                return value.GetRawText();
            }
        }

        return string.Empty;
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
