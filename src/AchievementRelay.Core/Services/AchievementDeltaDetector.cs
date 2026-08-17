using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

/// <summary>
/// Detects achievement changes from durable identities. Provider timestamps
/// are used only to prove that an achievement was unlocked after the current
/// live-delivery epoch. Each app start or long monitoring interruption begins
/// a new epoch, so another device's offline progress is reconciled silently.
/// </summary>
public static class AchievementDeltaDetector
{
    public static AchievementDeltaResult Detect(
        int previousReportedCount,
        IReadOnlyCollection<string>? previousAchievementIds,
        int currentReportedCount,
        IReadOnlyList<AchievementEvent> currentAchievements,
        DateTimeOffset deliveryEpochUtc,
        DateTimeOffset observedAt,
        TimeSpan futureClockTolerance,
        bool allowUntimestampedIdentityDelta)
    {
        ArgumentNullException.ThrowIfNull(currentAchievements);
        if (previousReportedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousReportedCount));
        }

        if (currentReportedCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentReportedCount));
        }

        var current = currentAchievements
            .Where(achievement => !string.IsNullOrWhiteSpace(achievement.Id))
            .GroupBy(achievement => achievement.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var currentIds = current
            .Select(achievement => achievement.Id)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        if (current.Length != currentReportedCount)
        {
            return new AchievementDeltaResult(
                IsComplete: false,
                NewAchievements: [],
                CurrentAchievementIds: currentIds,
                IdentityBaselineEstablished: false,
                UnidentifiedIncrease: Math.Max(0, currentReportedCount - previousReportedCount));
        }

        if (previousAchievementIds is not null)
        {
            var knownIds = new HashSet<string>(
                previousAchievementIds.Where(id => !string.IsNullOrWhiteSpace(id)),
                StringComparer.Ordinal);
            var newlyObserved = current
                .Where(achievement => !knownIds.Contains(achievement.Id))
                .ToArray();
            var durableIds = knownIds
                .Concat(currentIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var reportedIncrease = Math.Max(0, currentReportedCount - previousReportedCount);

            // A rising summary count with too few new detail identities means
            // the provider's summary and detail endpoints have not converged.
            if (newlyObserved.Length < reportedIncrease)
            {
                return new AchievementDeltaResult(
                    IsComplete: false,
                    NewAchievements: [],
                    CurrentAchievementIds: durableIds,
                    IdentityBaselineEstablished: true,
                    UnidentifiedIncrease: reportedIncrease - newlyObserved.Length);
            }

            // If a provider route changes how it represents identity, it can
            // make several historical items look new while the summary rose by
            // only one (or did not rise at all). Never flood those items. Keep
            // both identity forms as the durable baseline and let later real
            // count increases be detected normally.
            if (newlyObserved.Length > reportedIncrease)
            {
                return new AchievementDeltaResult(
                    IsComplete: true,
                    NewAchievements: [],
                    CurrentAchievementIds: durableIds,
                    IdentityBaselineEstablished: true,
                    UnidentifiedIncrease: reportedIncrease);
            }

            // Even with a durable identity set, a provider correction can
            // expose an old identity for the first time. A real historical
            // timestamp at or before the live-delivery epoch is conclusive:
            // retain the identity, but never send it to Discord. Missing or
            // unusable timestamps are eligible only when the coordinator has
            // durable proof that the count change was first observed after a
            // successful poll in an uninterrupted live session.
            var latestAcceptedTime = observedAt + futureClockTolerance;
            var deliverable = OrderForDelivery(newlyObserved.Where(achievement =>
                achievement.UnlockedAt is { } unlockedAt
                    ? unlockedAt > deliveryEpochUtc && unlockedAt <= latestAcceptedTime
                    : allowUntimestampedIdentityDelta));
            var historical = newlyObserved.Length - deliverable.Count;

            return new AchievementDeltaResult(
                IsComplete: true,
                NewAchievements: deliverable,
                CurrentAchievementIds: durableIds,
                IdentityBaselineEstablished: true,
                UnidentifiedIncrease: historical);
        }

        // A title absent from the original title-history page, or a snapshot
        // written before identity tracking existed, has counts but no verified
        // ID set. Never infer new events from a count or Gamerscore delta: that
        // can turn a newly revealed old game into a complete Discord backlog.
        // Only a usable provider timestamp strictly after the live-delivery
        // epoch can prove an event is new. Everything else becomes the
        // silent identity baseline; later set differences are exact.
        var increase = Math.Max(0, currentReportedCount - previousReportedCount);
        var latestAcceptedTime = observedAt + futureClockTolerance;
        var provenLive = current
            .Where(achievement => achievement.UnlockedAt is { } unlockedAt &&
                                  unlockedAt > deliveryEpochUtc &&
                                  unlockedAt <= latestAcceptedTime)
            .ToArray();

        if (provenLive.Length > increase)
        {
            return new AchievementDeltaResult(
                IsComplete: true,
                NewAchievements: [],
                CurrentAchievementIds: currentIds,
                IdentityBaselineEstablished: true,
                UnidentifiedIncrease: increase);
        }

        var unidentified = Math.Max(0, increase - provenLive.Length);

        return new AchievementDeltaResult(
            IsComplete: true,
            NewAchievements: OrderForDelivery(provenLive),
            CurrentAchievementIds: currentIds,
            IdentityBaselineEstablished: true,
            UnidentifiedIncrease: unidentified);
    }

    private static IReadOnlyList<AchievementEvent> OrderForDelivery(IEnumerable<AchievementEvent> achievements) =>
        achievements
            .OrderBy(achievement => achievement.UnlockedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(achievement => achievement.Id, StringComparer.Ordinal)
            .ToArray();
}

public sealed record AchievementDeltaResult(
    bool IsComplete,
    IReadOnlyList<AchievementEvent> NewAchievements,
    IReadOnlyList<string> CurrentAchievementIds,
    bool IdentityBaselineEstablished,
    int UnidentifiedIncrease);
