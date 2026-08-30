using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class OpenXblResponseParser
{
    private static readonly DateTimeOffset EarliestCredibleAchievementUtc =
        new(2005, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
                : GetString(item, "lastTimePlayed", "lastPlayed", "lastUnlock");
            if (DateTimeOffset.TryParse(
                    lastPlayedValue,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var parsedLastPlayed))
            {
                lastPlayedAt = parsedLastPlayed;
            }

            var devices = new List<string>();
            AddStringValues(item, devices, "devices", "platforms");

            parsed.Add(new XboxTitleProgress
            {
                TitleId = titleId.Trim(),
                Name = NullIfWhiteSpace(GetString(item, "name", "titleName")),
                CurrentAchievements = GetFirstNonNegativeInteger(
                    achievement,
                    "currentAchievements",
                    "earnedAchievements"),
                CurrentGamerscore = GetNonNegativeInteger(achievement, "currentGamerscore"),
                LastPlayedAt = lastPlayedAt,
                Devices = XboxPlatformClassifier.NormalizeDevices(devices),
                DisplayImageUrl = NormalizeUrlHint(FirstNonEmpty(
                    GetString(item, "displayImage", "displayImageUrl"),
                    titleHistory.ValueKind == JsonValueKind.Object
                        ? GetString(titleHistory, "displayImage", "displayImageUrl")
                        : string.Empty))
            });
        }

        return parsed
            .GroupBy(item => item.TitleId, StringComparer.Ordinal)
            .Select(group =>
            {
                var selected = group.OrderByDescending(item => item.LastPlayedAt).First();
                return selected with
                {
                    Devices = XboxPlatformClassifier.NormalizeDevices(
                        group.SelectMany(item => item.Devices)),
                    DisplayImageUrl = group
                        .Select(item => item.DisplayImageUrl)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                };
            })
            .OrderByDescending(item => item.LastPlayedAt)
            .ThenBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<AchievementEvent> ParseAchievements(
        string json,
        string accountId,
        string? fallbackTitleId = null,
        string? platformHint = null)
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
            var achievement = ParseAchievement(item, accountId, fallbackTitleId, platformHint);
            if (achievement is not null)
            {
                parsed.Add(achievement);
            }
        }

        return parsed
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.UnlockedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
    }

    public static string? ParseContinuationToken(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        return FindContinuationToken(ParseJsonRoot(json), 0);
    }

    private static AchievementEvent? ParseAchievement(
        JsonElement item,
        string accountId,
        string? fallbackTitleId,
        string? platformHint)
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

        DateTimeOffset? unlockedAt = null;
        if (DateTimeOffset.TryParse(
                unlockedValue,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedUnlockedAt) &&
            parsedUnlockedAt >= EarliestCredibleAchievementUtc)
        {
            unlockedAt = parsedUnlockedAt;
        }

        var achievementId = GetString(item, "id");
        var serviceConfigId = GetString(item, "serviceConfigId", "scid");
        var name = GetString(item, "name");
        if (string.IsNullOrWhiteSpace(achievementId))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Xbox achievement {achievementId}";
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

        var rarity = GetRarity(item);

        return new AchievementEvent
        {
            Id = CreateIdentity(accountId, serviceConfigId, titleId, achievementId),
            Name = name,
            Description = NullIfWhiteSpace(GetString(item, "unlockedDescription", "description")),
            GameName = NullIfWhiteSpace(gameName),
            Gamerscore = GetGamerscore(item),
            IsRare = rarity.IsRare,
            RarityKnown = rarity.Known,
            RarityPercentage = rarity.Percentage,
            HeroImageUrl = NormalizeUrlHint(GetHeroImageUrl(item)),
            ImageUrl = NormalizeUrlHint(GetImageUrl(item)),
            SourceProvider = "OpenXBL",
            Platform = GetPlatform(item, associations, platformHint),
            UnlockedAt = unlockedAt,
            UnlockTimeEstimated = unlockedAt is null
        };
    }

    private static JsonElement? GetArray(JsonElement root, params string[] propertyNames) =>
        GetArray(root, 0, propertyNames);

    private static string? FindContinuationToken(JsonElement element, int depth)
    {
        if (depth > 6)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String && depth < 2)
        {
            var nestedJson = element.GetString();
            if (!string.IsNullOrWhiteSpace(nestedJson))
            {
                try
                {
                    using var nestedDocument = JsonDocument.Parse(nestedJson.Trim().TrimStart('\uFEFF'));
                    return FindContinuationToken(nestedDocument.RootElement, depth + 1);
                }
                catch (JsonException)
                {
                    return null;
                }
            }

            return null;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindContinuationToken(item, depth + 1);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }

            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (TryGetProperty(element, "continuationToken", out var token) && token.ValueKind == JsonValueKind.String)
        {
            return NullIfWhiteSpace(token.GetString() ?? string.Empty);
        }

        foreach (var property in element.EnumerateObject())
        {
            var nested = FindContinuationToken(property.Value, depth + 1);
            if (!string.IsNullOrWhiteSpace(nested))
            {
                return nested;
            }
        }

        return null;
    }

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

    private static int GetFirstNonNegativeInteger(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetInteger(element, propertyName, out var value))
            {
                return Math.Max(0, value);
            }
        }

        return 0;
    }

    private static RarityMetadata GetRarity(JsonElement item)
    {
        bool? providerRare = null;
        if (TryGetProperty(item, "isRare", out var isRare) &&
            isRare.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            providerRare = isRare.GetBoolean();
        }

        double? percentage = null;
        string? category = null;
        if (TryGetProperty(item, "rarity", out var rarity))
        {
            if (rarity.ValueKind == JsonValueKind.Object)
            {
                category = NullIfWhiteSpace(GetString(rarity, "currentCategory", "category"));
                if (TryGetValidPercentage(rarity, "currentPercentage", out var nestedPercentage))
                {
                    percentage = nestedPercentage;
                }

                if (percentage is null &&
                    TryGetValidPercentage(rarity, "percentage", out var alternatePercentage))
                {
                    percentage = alternatePercentage;
                }
            }
            else if (rarity.ValueKind is JsonValueKind.Number or JsonValueKind.String)
            {
                if (percentage is null && TryParseValidPercentage(rarity, out var scalarPercentage))
                {
                    percentage = scalarPercentage;
                }
                else if (rarity.ValueKind == JsonValueKind.String)
                {
                    category = NullIfWhiteSpace(rarity.GetString() ?? string.Empty);
                }
            }
        }

        if (percentage is null &&
            TryGetValidPercentage(item, "rarityPercentage", out var directPercentage))
        {
            percentage = directPercentage;
        }

        bool? categoryRare = null;
        if (category?.Equals("Rare", StringComparison.OrdinalIgnoreCase) == true)
        {
            categoryRare = true;
        }
        else if (category?.Equals("Common", StringComparison.OrdinalIgnoreCase) == true)
        {
            categoryRare = false;
        }

        var known = providerRare is not null || categoryRare is not null || percentage is not null;
        var rare = percentage is { } value
            ? RelayRarityClassifier.Classify(value) is RelayRarityTier.Gold or RelayRarityTier.Platinum
            : providerRare ?? categoryRare ?? false;
        return new RarityMetadata(known, rare, percentage);
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

                var type = GetString(asset, "type");
                if (type.Equals("Icon", StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }

                if (!type.Equals("Background", StringComparison.OrdinalIgnoreCase) &&
                    !type.Equals("Hero", StringComparison.OrdinalIgnoreCase))
                {
                    fallback = string.IsNullOrWhiteSpace(fallback) ? url : fallback;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }
        }

        return GetString(item, "imageUrl", "image");
    }

    private static string GetHeroImageUrl(JsonElement item)
    {
        if (!TryGetProperty(item, "mediaAssets", out var assets) ||
            assets.ValueKind != JsonValueKind.Array)
        {
            return GetString(item, "backgroundImageUrl", "heroImageUrl");
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var type = GetString(asset, "type");
            if (type.Equals("Background", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("Hero", StringComparison.OrdinalIgnoreCase))
            {
                var url = GetString(asset, "url");
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }
        }

        return GetString(item, "backgroundImageUrl", "heroImageUrl");
    }

    private static string? GetPlatform(
        JsonElement item,
        JsonElement titleAssociations,
        string? platformHint)
    {
        // unlockedOnline is specific to the legacy Xbox achievement shape and
        // remains authoritative when a backwards-compatible title is played
        // on newer console hardware.
        if (TryGetProperty(item, "unlockedOnline", out _))
        {
            return "Xbox 360";
        }

        var direct = GetString(item, "platform", "earnedPlatform", "deviceType", "device");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return XboxPlatformClassifier.Classify(direct) ?? "Xbox";
        }

        var hinted = XboxPlatformClassifier.Classify(platformHint);
        if (hinted is not null)
        {
            return hinted;
        }

        var available = new List<string>();
        AddStringValues(item, available, "platforms", "devices");
        if (titleAssociations.ValueKind == JsonValueKind.Array)
        {
            foreach (var association in titleAssociations.EnumerateArray())
            {
                AddStringValues(association, available, "platforms", "devices");
            }
        }

        return available.Count == 0
            ? null
            : XboxPlatformClassifier.Classify(null, available) ?? "Xbox";
    }

    private static void AddStringValues(
        JsonElement element,
        ICollection<string> destination,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out var values))
            {
                continue;
            }

            if (values.ValueKind != JsonValueKind.Array)
            {
                if (values.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    destination.Add("Unknown");
                }

                continue;
            }

            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    destination.Add(value.GetString()!);
                }
                else if (value.ValueKind == JsonValueKind.Number)
                {
                    destination.Add(value.GetRawText());
                }
                else if (value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                {
                    destination.Add("Unknown");
                }
            }
        }
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

    private static bool TryGetValidPercentage(JsonElement element, string propertyName, out double value)
    {
        value = 0;
        if (!TryGetProperty(element, propertyName, out var property))
        {
            return false;
        }

        return TryParseValidPercentage(property, out value);
    }

    private static bool TryParseValidPercentage(JsonElement property, out double value)
    {
        value = 0;
        var parsed = property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false
        };

        return parsed && double.IsFinite(value) && value is >= 0 and <= 100;
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

    private static string? NormalizeUrlHint(string? value)
    {
        const int maximumLength = 2048;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : null;
    }

    private readonly record struct RarityMetadata(
        bool Known,
        bool IsRare,
        double? Percentage);
}
