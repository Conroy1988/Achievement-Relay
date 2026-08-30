namespace AchievementRelay.Core.Models;

public sealed record XboxTitleProgress
{
    public required string TitleId { get; init; }

    public string? Name { get; init; }

    public int CurrentAchievements { get; init; }

    public int CurrentGamerscore { get; init; }

    public DateTimeOffset? LastPlayedAt { get; init; }

    public IReadOnlyList<string> Devices { get; init; } = [];

    public string? DisplayImageUrl { get; init; }
}
