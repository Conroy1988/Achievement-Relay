namespace AchievementRelay.App.Services;

/// <summary>Read-only projection; never queues or changes achievement delivery.</summary>
public sealed record RedlineDashboardSummary(int TodayUnlocks, double? RarestPercentage, int NeedsAttention)
{
    public static RedlineDashboardSummary Create(IEnumerable<JournalEntry> history, DateTimeOffset now, TimeZoneInfo zone)
    {
        var live = history.Where(x => !x.Achievement.IsHistorical && !x.Achievement.IsGameCompletion && x.ObservedAt <= now)
            .GroupBy(x => x.Achievement.Id).Select(g => g.MaxBy(x => x.UpdatedAt)!).ToArray();
        var date = TimeZoneInfo.ConvertTime(now, zone).Date;
        var today = live.Where(x => TimeZoneInfo.ConvertTime(x.ObservedAt, zone).Date == date).ToArray();
        var rarity = today.Where(x => x.Achievement.RarityKnown)
            .Select(x => x.Achievement.RarityPercentage)
            .Where(x => x is >= 0 and <= 100).Min();
        return new(today.Length, rarity, live.Count(x => x.Delivery is not ("Delivered" or "Delivered on another PC" or "Filtered")));
    }
}
