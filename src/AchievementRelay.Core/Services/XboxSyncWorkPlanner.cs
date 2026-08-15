using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class XboxSyncWorkPlanner
{
    public static bool IsBackgroundWorkDue(
        DateTimeOffset? lastBackgroundWorkUtc,
        DateTimeOffset now,
        TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        return lastBackgroundWorkUtc is null || now - lastBackgroundWorkUtc.Value >= interval;
    }

    public static XboxTitleSyncWork? SelectNext(
        IEnumerable<XboxTitleSyncWork> pendingWork,
        bool allowBackground)
    {
        ArgumentNullException.ThrowIfNull(pendingWork);

        return pendingWork
            .Where(item => !string.IsNullOrWhiteSpace(item.TitleId))
            .Where(item => allowBackground || item.IsPriority)
            .OrderByDescending(item => item.IsPriority)
            .ThenByDescending(item => item.LastPlayedAt)
            .ThenBy(item => item.FirstObservedUtc)
            .ThenBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.TitleId, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
