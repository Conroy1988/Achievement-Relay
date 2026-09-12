using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public enum OverlayMotionMode { Static, Fade, Full }

public static class OverlayMotionPolicy
{
    public static OverlayMotionMode Resolve(AppSettings settings, bool windowsAnimations, bool highContrast)
    {
        if (!settings.AchievementOverlayAnimationEnabled || highContrast ||
            (settings.AchievementOverlayFollowWindowsMotion && !windowsAnimations))
            return OverlayMotionMode.Static;
        return settings.AchievementOverlayReducedMotion ? OverlayMotionMode.Fade : OverlayMotionMode.Full;
    }

    public static string Describe(AppSettings settings, bool windowsAnimations, bool highContrast)
    {
        if (highContrast) return "Static — Windows high-contrast mode is active.";
        if (!settings.AchievementOverlayAnimationEnabled) return "Static — Animate unlocks is off.";
        if (settings.AchievementOverlayFollowWindowsMotion && !windowsAnimations)
            return "Static — following Windows, where animation is disabled.";
        var mode = settings.AchievementOverlayReducedMotion ? "Reduced motion — fade only." : "Full animation — sweep, pulse, shimmer and countdown.";
        return !windowsAnimations ? mode + " Windows animation is off; Relay is using your settings above." : mode;
    }
}
