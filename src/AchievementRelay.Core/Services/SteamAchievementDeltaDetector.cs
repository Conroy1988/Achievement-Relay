using System.Security.Cryptography;
using System.Text;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class SteamAchievementDeltaDetector
{
    public static SteamAchievementDelta Detect(
        IReadOnlyCollection<string>? previousUnlockedApiNames,
        IReadOnlyCollection<SteamAchievementObservation> currentAchievements,
        IReadOnlyCollection<string>? observedUnlockedTransitions,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(currentAchievements);

        var unlocked = currentAchievements
            .Where(item => item.IsUnlocked && !string.IsNullOrWhiteSpace(item.ApiName))
            .GroupBy(item => item.ApiName, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var currentIds = unlocked
            .Select(item => item.ApiName)
            .ToHashSet(StringComparer.Ordinal);
        var previousIds = previousUnlockedApiNames?.ToHashSet(StringComparer.Ordinal) ??
                          new HashSet<string>(StringComparer.Ordinal);
        var transitionIds = observedUnlockedTransitions?
                                .Where(value => !string.IsNullOrWhiteSpace(value))
                                .ToHashSet(StringComparer.Ordinal) ??
                            new HashSet<string>(StringComparer.Ordinal);

        // Merely appearing unlocked is always history, including on every
        // initial helper snapshot. Eligibility requires a direct live signal:
        // either the helper observed locked -> unlocked in memory, or Steam
        // emitted its completed-achievement callback during this helper
        // lifetime. Unlock timestamps are display metadata and can never
        // authorize delivery, which keeps restarts and account switches safe.
        var newUnlocks = unlocked
            .Where(item => !previousIds.Contains(item.ApiName) &&
                           transitionIds.Contains(item.ApiName))
            .OrderBy(item => item.UnlockedAt ?? observedAt)
            .ThenBy(item => item.ApiName, StringComparer.Ordinal)
            .ToArray();
        return new SteamAchievementDelta(newUnlocks, currentIds, previousUnlockedApiNames is null);
    }

    public static string CreateEventId(string steamId, uint appId, string achievementApiName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(steamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(achievementApiName);

        var input = $"steam\0{steamId.Trim()}\0{appId}\0{achievementApiName.Trim()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
