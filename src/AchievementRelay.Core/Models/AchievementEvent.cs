namespace AchievementRelay.Core.Models;

public sealed record AchievementEvent
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public string? GameName { get; init; }

    public int? Gamerscore { get; init; }

    public bool IsRare { get; init; }

    /// <summary>
    /// False when the provider could not supply rarity metadata. Rare-only
    /// filtering must not discard an otherwise valid unlock in that case.
    /// </summary>
    public bool RarityKnown { get; init; }

    public double? RarityPercentage { get; init; }

    /// <summary>
    /// Optional wide game or achievement artwork used behind the Collector
    /// Card. This remains separate from ImageUrl, which is the foreground
    /// achievement icon and the legacy Discord thumbnail fallback.
    /// </summary>
    public string? HeroImageUrl { get; init; }

    public string? ImageUrl { get; init; }

    public byte[]? ImageBytes { get; init; }

    public string? ImageFileName { get; init; }

    public string? ImageContentType { get; init; }

    public string? PlayerName { get; init; }

    public required string SourceProvider { get; init; }

    /// <summary>
    /// User-facing source platform such as Steam, Xbox PC, Xbox Console or
    /// Xbox 360. Null means the provider did not supply enough evidence to be
    /// more specific than SourceProvider.
    /// </summary>
    public string? Platform { get; init; }

    /// <summary>
    /// True when ImageBytes contains the complete generated Collector Card
    /// and Discord should present it as the embed's large image rather than a
    /// provider thumbnail.
    /// </summary>
    public bool IsCollectorCard { get; init; }

    /// <summary>
    /// The unlock time reported by the source platform. Legacy Xbox responses
    /// and some Steam states can omit a usable value, so event identity and
    /// delivery must never depend on this property being present.
    /// </summary>
    public DateTimeOffset? UnlockedAt { get; init; }

    /// <summary>
    /// True when Achievement Relay had to use its observation time for the
    /// Discord timestamp because the provider did not supply a usable unlock
    /// time.
    /// </summary>
    public bool UnlockTimeEstimated { get; init; }

}
