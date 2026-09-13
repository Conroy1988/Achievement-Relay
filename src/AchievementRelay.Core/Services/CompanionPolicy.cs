using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class CompanionPolicy
{
    public static string GameKey(AchievementEvent achievement) =>
        string.Concat(achievement.SourceProvider, ":", achievement.GameName ?? "Unknown");

    public static AppSettings ForGame(AchievementEvent achievement, AppSettings settings)
    {
        var rule = (settings.Companion.Games ?? []).FirstOrDefault(x => x.GameKey == GameKey(achievement));
        if (rule is null) return settings;
        var hide = rule.HideOverlay || (rule.RareCelebrationsOnly && achievement.RarityKnown && !achievement.IsRare);
        return settings with
        {
            AchievementOverlayEnabled = settings.AchievementOverlayEnabled && !hide,
            AchievementOverlaySoundEnabled = settings.AchievementOverlaySoundEnabled && !rule.MuteSound
        };
    }

    public static double Bounded(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
}
