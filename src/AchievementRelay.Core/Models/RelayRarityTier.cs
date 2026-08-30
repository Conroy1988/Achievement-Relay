namespace AchievementRelay.Core.Models;

/// <summary>
/// Achievement Relay's presentation tier derived from a provider's exact
/// global player unlock percentage. These labels are intentionally separate
/// from any platform-owned rarity classification.
/// </summary>
public enum RelayRarityTier
{
    Unranked,
    Bronze,
    Silver,
    Gold,
    Platinum
}
