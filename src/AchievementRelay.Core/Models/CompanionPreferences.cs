namespace AchievementRelay.Core.Models;

public enum DiscordPresentation { Showcase, Compact }
public enum UnlockSoundPack { Signal, Glass, Arcade }
public sealed record GamePreferences
{
    public string GameKey { get; init; } = "";
    public bool MuteSound { get; init; }
    public bool HideOverlay { get; init; }
    public bool RareCelebrationsOnly { get; init; }
}

public sealed record CompanionPreferences
{
    public DiscordPresentation DiscordPresentation { get; init; }
    public bool RarityCelebrations { get; init; } = true;
    public double OverlayScale { get; init; } = 1;
    public int OverlaySeconds { get; init; } = 5;
    public double OverlayX { get; init; } = .5;
    public double OverlayY { get; init; }
    public string OverlayMonitor { get; init; } = "";
    public string SharedDeliveryFolder { get; init; } = "";
    public GamePreferences[] Games { get; init; } = [];
    public UnlockSoundPack SoundPack { get; init; }
    public UnlockSoundPack RareSoundPack { get; init; } = UnlockSoundPack.Glass;
    public int SoundVolume { get; init; } = 20;
    public bool SoundStudioEnabled { get; init; }
    public bool ImportHistory { get; init; }
    public string[] PinnedAchievements { get; init; } = [];
}
