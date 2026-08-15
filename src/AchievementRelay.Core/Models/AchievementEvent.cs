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

    public required string SourceProvider { get; init; }

    /// <summary>
    /// The unlock time reported by Xbox/OpenXBL. Legacy Xbox 360 responses can
    /// legitimately omit this value or return the .NET sentinel date, so event
    /// identity and delivery must never depend on this property being present.
    /// </summary>
    public DateTimeOffset? UnlockedAt { get; init; }

    /// <summary>
    /// True when Achievement Relay had to use its observation time for the
    /// Discord timestamp because the provider did not supply a usable unlock
    /// time.
    /// </summary>
    public bool UnlockTimeEstimated { get; init; }

}
