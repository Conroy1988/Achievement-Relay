namespace AchievementRelay.Core.Models;

public sealed record AchievementEvent
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? GameName { get; init; }

    public int? Gamerscore { get; init; }

    public bool IsRare { get; init; }

    public string? ImageUrl { get; init; }

    public required string SourceApplication { get; init; }

    public required string SourcePackageFamilyName { get; init; }

    public required DateTimeOffset UnlockedAt { get; init; }

    public IReadOnlyList<string> RawTextElements { get; init; } = [];
}
