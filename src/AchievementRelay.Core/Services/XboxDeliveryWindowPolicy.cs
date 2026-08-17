namespace AchievementRelay.Core.Services;

/// <summary>
/// Defines the uninterrupted monitoring window in which Xbox unlocks are
/// eligible for live delivery. Starting the app, resuming after a long gap,
/// or detecting a backwards clock jump begins a fresh delivery epoch.
/// </summary>
public static class XboxDeliveryWindowPolicy
{
    public static readonly TimeSpan MinimumContinuity = TimeSpan.FromMinutes(10);

    public static XboxDeliveryWindowDecision Resolve(
        DateTimeOffset? currentEpochUtc,
        DateTimeOffset? lastSessionSuccessfulPollUtc,
        DateTimeOffset observedAt,
        int pollIntervalSeconds)
    {
        var normalInterval = TimeSpan.FromSeconds(Math.Clamp(pollIntervalSeconds, 60, 3600));
        var continuityLimit = normalInterval + normalInterval + TimeSpan.FromMinutes(1);
        if (continuityLimit < MinimumContinuity)
        {
            continuityLimit = MinimumContinuity;
        }

        var epoch = currentEpochUtc ?? observedAt;
        if (lastSessionSuccessfulPollUtc is not { } lastSuccessfulPoll)
        {
            return new XboxDeliveryWindowDecision(epoch, false, false);
        }

        var elapsed = observedAt - lastSuccessfulPoll;
        if (elapsed < TimeSpan.Zero || elapsed > continuityLimit)
        {
            return new XboxDeliveryWindowDecision(observedAt, false, true);
        }

        return new XboxDeliveryWindowDecision(epoch, true, false);
    }
}

public readonly record struct XboxDeliveryWindowDecision(
    DateTimeOffset EpochUtc,
    bool HasPriorSuccessfulPoll,
    bool ReconciledAfterGap);
