namespace AchievementRelay.Core.Models;

public sealed record SteamAchievementObservation
{
    public required string ApiName { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool IsUnlocked { get; init; }

    public DateTimeOffset? UnlockedAt { get; init; }

    public bool IsHidden { get; init; }

    public byte[]? IconRgba { get; init; }

    public int IconWidth { get; init; }

    public int IconHeight { get; init; }
}

public sealed record SteamAchievementDelta(
    IReadOnlyList<SteamAchievementObservation> NewAchievements,
    IReadOnlySet<string> CurrentUnlockedApiNames,
    bool BaselineEstablished);
