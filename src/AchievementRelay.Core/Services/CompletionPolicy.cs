namespace AchievementRelay.Core.Services;

public static class CompletionPolicy
{
    public static bool IsVerifiedTransition(int? previousCount, int currentCount, int? total,
        bool hasEligibleLiveUnlock, bool completeDetails) =>
        previousCount is >= 0 && total is > 0 && previousCount < total && currentCount == total &&
        hasEligibleLiveUnlock && completeDetails;
}
