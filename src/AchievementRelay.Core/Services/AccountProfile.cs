using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

/// <summary>Explicit portable settings contract. Never serialize the local settings file for upload.</summary>
public static class AccountProfile
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Shared = ["xboxUserId", "xboxGamertag", "displayName", "postRareOnly",
        "achievementOverlayEnabled", "achievementOverlayAnimationEnabled", "achievementOverlayReducedMotion",
        "achievementOverlaySoundEnabled", "achievementOverlayVolume", "steamEnabled",
        "includeRawDetailsWhenUncertain", "discordUsername", "pollIntervalSeconds"];
    private static readonly string[] Companion = ["discordPresentation", "rarityCelebrations", "overlayScale",
        "overlaySeconds", "games", "soundPack", "rareSoundPack", "soundVolume", "soundStudioEnabled",
        "importHistory", "pinnedAchievements"];

    public static JsonObject Capture(AppSettings settings, string? webhook, string? apiKey)
    {
        var source = JsonSerializer.SerializeToNode(settings, Json)!.AsObject();
        var result = new JsonObject();
        foreach (var key in Shared) result[key] = source[key]?.DeepClone();
        var companion = source["companion"]!.AsObject();
        foreach (var key in Companion) result["companion." + key] = companion[key]?.DeepClone();
        result["webhook"] = webhook ?? "";
        result["openXblKey"] = apiKey ?? "";
        return result;
    }

    public static AppSettings Apply(AppSettings local, JsonObject profile)
    {
        var result = JsonSerializer.SerializeToNode(local, Json)!.AsObject();
        foreach (var key in Shared)
            if (profile.TryGetPropertyValue(key, out var value)) result[key] = value?.DeepClone();
        var companion = result["companion"]!.AsObject();
        foreach (var key in Companion)
            if (profile.TryGetPropertyValue("companion." + key, out var value)) companion[key] = value?.DeepClone();
        return result.Deserialize<AppSettings>(Json) ?? throw new JsonException("Invalid account profile.");
    }

    // Only locally changed fields replace remote fields. The successful CAS orders concurrent writes.
    public static JsonObject Merge(JsonObject local, JsonObject remote, JsonObject? baseline)
    {
        var result = (JsonObject)remote.DeepClone();
        foreach (var (key, value) in local)
        {
            if (!remote.ContainsKey(key) || baseline is not null && !JsonNode.DeepEquals(value, baseline[key]))
                result[key] = value?.DeepClone();
        }
        const string pins = "companion.pinnedAchievements";
        if (local[pins] is JsonArray localPins && remote[pins] is JsonArray remotePins)
        {
            var before = (baseline?[pins] as JsonArray ?? []).Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var here = localPins.Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            var there = remotePins.Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            there.ExceptWith(before.Except(here));
            there.UnionWith(here.Except(before));
            result[pins] = new JsonArray(there.Order(StringComparer.Ordinal).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
        }
        string[] connection = ["xboxUserId", "xboxGamertag", "openXblKey"];
        if (baseline is not null && connection.Any(key => !JsonNode.DeepEquals(local[key], baseline[key])))
            foreach (var key in connection) result[key] = local[key]?.DeepClone();
        return result;
    }
}

/// <summary>Versioned, account-bound authenticated encryption; recovery keys never go to Supabase.</summary>
public static class AccountCipher
{
    public const int MaximumPlaintextBytes = 1_500_000;
    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);
    public static string Encrypt(byte[] plaintext, byte[] key, Guid userId)
    {
        if (plaintext.Length > MaximumPlaintextBytes) throw new InvalidDataException("Cloud profile is too large.");
        var result = new byte[1 + 12 + 16 + plaintext.Length];
        result[0] = 1;
        RandomNumberGenerator.Fill(result.AsSpan(1, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(1, 12), plaintext, result.AsSpan(29), result.AsSpan(13, 16), AssociatedData(userId));
        return Convert.ToBase64String(result);
    }
    public static byte[] Decrypt(string envelope, byte[] key, Guid userId)
    {
        if (envelope.Length > 2_000_040) throw new InvalidDataException("Cloud profile is too large.");
        var input = Convert.FromBase64String(envelope);
        if (input.Length < 29 || input[0] != 1) throw new InvalidDataException("Unsupported cloud profile.");
        var plaintext = new byte[input.Length - 29];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(input.AsSpan(1, 12), input.AsSpan(29), input.AsSpan(13, 16), plaintext, AssociatedData(userId));
        return plaintext;
    }
    private static byte[] AssociatedData(Guid userId) => Encoding.UTF8.GetBytes("AchievementRelay.account.v1:" + userId.ToString("D"));
}
