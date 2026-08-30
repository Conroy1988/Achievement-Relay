using AchievementRelay.Core.Models;
using System.Globalization;

namespace AchievementRelay.Core.Services;

public static class RelayRarityClassifier
{
    public static RelayRarityTier Classify(double? globalUnlockPercentage)
    {
        if (globalUnlockPercentage is not { } percentage ||
            !double.IsFinite(percentage) ||
            percentage is < 0 or > 100)
        {
            return RelayRarityTier.Unranked;
        }

        return percentage switch
        {
            < 3 => RelayRarityTier.Platinum,
            < 10 => RelayRarityTier.Gold,
            < 25 => RelayRarityTier.Silver,
            _ => RelayRarityTier.Bronze
        };
    }

    public static string DisplayName(RelayRarityTier tier) => tier switch
    {
        RelayRarityTier.Bronze => "Bronze",
        RelayRarityTier.Silver => "Silver",
        RelayRarityTier.Gold => "Gold",
        RelayRarityTier.Platinum => "Platinum",
        _ => "Unranked"
    };

    public static string Description(RelayRarityTier tier) => tier switch
    {
        RelayRarityTier.Bronze => "Widely unlocked",
        RelayRarityTier.Silver => "Uncommon unlock",
        RelayRarityTier.Gold => "Rare unlock",
        RelayRarityTier.Platinum => "Ultra-rare unlock",
        _ => "Global rarity unavailable"
    };

    public static string Range(RelayRarityTier tier) => tier switch
    {
        RelayRarityTier.Bronze => "25% or more",
        RelayRarityTier.Silver => "10–24.99%",
        RelayRarityTier.Gold => "3–9.99%",
        RelayRarityTier.Platinum => "under 3%",
        _ => "percentage unavailable"
    };

    public static string FormatPercentage(double? globalUnlockPercentage)
    {
        if (Classify(globalUnlockPercentage) == RelayRarityTier.Unranked ||
            globalUnlockPercentage is not { } percentage)
        {
            return "—%";
        }

        if (percentage > 0 && percentage < 0.01)
        {
            return "<0.01%";
        }

        var tier = Classify(percentage);
        for (var decimalPlaces = 2; decimalPlaces <= 6; decimalPlaces++)
        {
            var format = string.Concat("0.", new string('#', decimalPlaces));
            var candidate = percentage.ToString(format, CultureInfo.InvariantCulture);
            if (double.TryParse(
                    candidate,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var displayedPercentage) &&
                Classify(displayedPercentage) == tier)
            {
                return string.Concat(candidate, "%");
            }
        }

        // A value can sit so close to a boundary that even six displayed
        // decimal places would round into the next tier. Keep the compact card
        // honest without exposing noisy floating-point tails.
        return tier switch
        {
            RelayRarityTier.Platinum => "<3%",
            RelayRarityTier.Gold => "<10%",
            RelayRarityTier.Silver => "<25%",
            _ => string.Concat(percentage.ToString("0.##", CultureInfo.InvariantCulture), "%")
        };
    }
}
