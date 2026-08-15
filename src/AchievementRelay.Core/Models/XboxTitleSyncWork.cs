namespace AchievementRelay.Core.Models;

/// <summary>
/// Durable per-title work discovered from the lightweight title index but not
/// yet committed to the exact achievement-identity baseline.
/// </summary>
public sealed record XboxTitleSyncWork
{
    public string TitleId { get; init; } = string.Empty;

    public string? Name { get; init; }

    public int CurrentAchievements { get; init; }

    public int CurrentGamerscore { get; init; }

    public DateTimeOffset? LastPlayedAt { get; init; }

    public DateTimeOffset FirstObservedUtc { get; init; }

    public DateTimeOffset LastObservedUtc { get; init; }

    /// <summary>
    /// True when this is a change to an identity-verified title or the title
    /// was played after monitoring began. Priority work is checked before
    /// low-priority historical baseline hydration.
    /// </summary>
    public bool IsPriority { get; init; }
}
