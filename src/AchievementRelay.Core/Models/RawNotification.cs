namespace AchievementRelay.Core.Models;

public sealed record RawNotification
{
    public required uint PlatformId { get; init; }

    public required string ApplicationDisplayName { get; init; }

    public required string PackageFamilyName { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public IReadOnlyList<string> TextElements { get; init; } = [];

    public string? ImageUrl { get; init; }
}
