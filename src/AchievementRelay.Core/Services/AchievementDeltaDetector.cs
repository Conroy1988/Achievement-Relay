using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

/// <summary>
/// Detects achievement changes from durable identities. Provider timestamps
/// are used only to bridge a pre-identity state written by older app versions.
/// </summary>
public static class AchievementDeltaDetector
{
    public static AchievementDeltaResult Detect(
        int previousReportedCount,
        IReadOnlyCollection<string>? previousAchievementIds,
        int previousReportedGamerscore,
        int currentReportedCount,
        int currentReportedGamerscore,
        IReadOnlyList<AchievementEvent> currentAchievements,
        DateTimeOffset previousSuccessfulPollUtc,
        DateTimeOffset observedAt,
        TimeSpan futureClockTolerance)
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

        if (previousReportedGamerscore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(previousReportedGamerscore));
        }

        if (currentReportedGamerscore < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentReportedGamerscore));
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
            var detected = OrderForDelivery(current.Where(achievement => !knownIds.Contains(achievement.Id)));
            var durableIds = knownIds
                .Concat(currentIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();
            var reportedIncrease = Math.Max(0, currentReportedCount - previousReportedCount);

            // A rising summary count with too few new detail identities means
            // the provider's summary and detail endpoints have not converged.
            if (detected.Count < reportedIncrease)
            {
                return new AchievementDeltaResult(
                    IsComplete: false,
                    NewAchievements: [],
                    CurrentAchievementIds: durableIds,
                    IdentityBaselineEstablished: true,
                    UnidentifiedIncrease: reportedIncrease - detected.Count);
            }

            // If a provider route changes how it represents identity, it can
            // make several historical items look new while the summary rose by
            // only one (or did not rise at all). Never flood those items. Keep
            // both identity forms as the durable baseline and let later real
            // count increases be detected normally.
            if (detected.Count > reportedIncrease)
            {
                return new AchievementDeltaResult(
                    IsComplete: true,
                    NewAchievements: [],
                    CurrentAchievementIds: durableIds,
                    IdentityBaselineEstablished: true,
                    UnidentifiedIncrease: reportedIncrease);
            }

            return new AchievementDeltaResult(
                IsComplete: true,
                NewAchievements: detected,
                CurrentAchievementIds: durableIds,
                IdentityBaselineEstablished: true,
                UnidentifiedIncrease: 0);
        }

        // Schema-v2 states contain counts but no identities. Use trustworthy
        // post-poll timestamps, plus a uniquely attributable untimestamped
        // remainder, for this one migration poll. The full current identity set
        // is then persisted and all later polls are timestamp-independent.
        var increase = Math.Max(0, currentReportedCount - previousReportedCount);
        if (increase == 0)
        {
            return new AchievementDeltaResult(
                IsComplete: true,
                NewAchievements: [],
                CurrentAchievementIds: currentIds,
                IdentityBaselineEstablished: true,
                UnidentifiedIncrease: 0);
        }

        var latestAcceptedTime = observedAt + futureClockTolerance;
        var timestamped = current
            .Where(achievement => achievement.UnlockedAt is { } unlockedAt &&
                                  unlockedAt > previousSuccessfulPollUtc &&
                                  unlockedAt <= latestAcceptedTime)
            .ToArray();

        if (timestamped.Length > increase)
        {
            return new AchievementDeltaResult(
                IsComplete: true,
                NewAchievements: [],
                CurrentAchievementIds: currentIds,
                IdentityBaselineEstablished: true,
                UnidentifiedIncrease: increase);
        }

        var remaining = increase - timestamped.Length;
        var timestampedIds = new HashSet<string>(
            timestamped.Select(achievement => achievement.Id),
            StringComparer.Ordinal);
        var unattributed = current
            .Where(achievement => !timestampedIds.Contains(achievement.Id))
            .ToArray();
        var untimestamped = unattributed
            .Where(achievement => achievement.UnlockedAt is null)
            .ToArray();
        var remainingGamerscore = currentReportedGamerscore - previousReportedGamerscore;
        if (timestamped.All(achievement => achievement.Gamerscore is not null))
        {
            remainingGamerscore -= timestamped.Sum(achievement => achievement.Gamerscore!.Value);
        }
        else
        {
            remainingGamerscore = -1;
        }

        IReadOnlyList<AchievementEvent> attributableRemainder = [];
        if (remaining > 0 && remainingGamerscore >= 0)
        {
            attributableRemainder = AttributeByCountAndGamerscore(
                untimestamped,
                remaining,
                remainingGamerscore);
            if (attributableRemainder.Count == 0)
            {
                // A delayed provider update can expose a usable but old time.
                // Fall back to all not-already-attributed identities only when
                // count and Gamerscore still yield exactly one possible set.
                attributableRemainder = AttributeByCountAndGamerscore(
                    unattributed,
                    remaining,
                    remainingGamerscore);
            }
        }
        var detectedDuringMigration = timestamped
            .Concat(attributableRemainder)
            .GroupBy(achievement => achievement.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        var unidentified = Math.Max(0, increase - detectedDuringMigration.Length);

        return new AchievementDeltaResult(
            IsComplete: true,
            NewAchievements: OrderForDelivery(detectedDuringMigration),
            CurrentAchievementIds: currentIds,
            IdentityBaselineEstablished: true,
            UnidentifiedIncrease: unidentified);
    }

    private static IReadOnlyList<AchievementEvent> OrderForDelivery(IEnumerable<AchievementEvent> achievements) =>
        achievements
            .OrderBy(achievement => achievement.UnlockedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(achievement => achievement.Id, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<AchievementEvent> AttributeByCountAndGamerscore(
        IReadOnlyList<AchievementEvent> candidates,
        int requiredCount,
        int requiredGamerscore)
    {
        if (candidates.Count == requiredCount &&
            candidates.All(achievement => achievement.Gamerscore is not null) &&
            candidates.Sum(achievement => achievement.Gamerscore!.Value) == requiredGamerscore)
        {
            return candidates;
        }

        return FindUniqueGamerscoreCombination(candidates, requiredCount, requiredGamerscore);
    }

    private static IReadOnlyList<AchievementEvent> FindUniqueGamerscoreCombination(
        IReadOnlyList<AchievementEvent> candidates,
        int requiredCount,
        int requiredGamerscore)
    {
        // Real polls normally increase by one. Bound the general search so a
        // malformed or unusually large provider response cannot create an
        // expensive combinatorial operation.
        if (requiredCount is < 1 or > 4 ||
            candidates.Count > 128 ||
            candidates.Any(achievement => achievement.Gamerscore is null))
        {
            return [];
        }

        List<AchievementEvent>? unique = null;
        var multiple = false;
        var selected = new List<AchievementEvent>(requiredCount);

        void Search(int start, int countRemaining, int scoreRemaining)
        {
            if (multiple || scoreRemaining < 0)
            {
                return;
            }

            if (countRemaining == 0)
            {
                if (scoreRemaining != 0)
                {
                    return;
                }

                if (unique is null)
                {
                    unique = [.. selected];
                }
                else
                {
                    multiple = true;
                }

                return;
            }

            for (var index = start; index <= candidates.Count - countRemaining; index++)
            {
                var candidate = candidates[index];
                selected.Add(candidate);
                Search(index + 1, countRemaining - 1, scoreRemaining - candidate.Gamerscore!.Value);
                selected.RemoveAt(selected.Count - 1);
                if (multiple)
                {
                    return;
                }
            }
        }

        Search(0, requiredCount, requiredGamerscore);
        return multiple || unique is null ? [] : unique;
    }
}

public sealed record AchievementDeltaResult(
    bool IsComplete,
    IReadOnlyList<AchievementEvent> NewAchievements,
    IReadOnlyList<string> CurrentAchievementIds,
    bool IdentityBaselineEstablished,
    int UnidentifiedIncrease);
