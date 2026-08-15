namespace AchievementRelay.Core.Services;

public enum OpenXblRequestPriority
{
    Essential,
    Background
}

public sealed record OpenXblRequestDecision(bool Allowed, TimeSpan RetryAfter);

/// <summary>
/// Protects the provider allowance even when callers retry, manually sync, or
/// encounter endpoints that require route negotiation and continuation pages.
/// Provider headers take precedence; a conservative local rolling-hour guard
/// remains active when those headers are absent.
/// </summary>
public sealed class OpenXblRequestBudget
{
    public const int LocalHourlySafetyCeiling = 120;

    private static readonly TimeSpan WindowLength = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinimumRetry = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _recentRequests = new();
    private int? _providerLimit;
    private int? _providerRemaining;
    private DateTimeOffset? _providerResetUtc;

    public OpenXblRequestDecision CanStartOperation(
        OpenXblRequestPriority priority,
        int maximumRequests,
        DateTimeOffset now)
    {
        if (maximumRequests <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRequests));
        }

        lock (_gate)
        {
            Refresh(now);
            return Evaluate(priority, maximumRequests, now);
        }
    }

    public OpenXblRequestDecision TryAcquire(
        OpenXblRequestPriority priority,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            Refresh(now);
            var decision = Evaluate(priority, 1, now);
            if (!decision.Allowed)
            {
                return decision;
            }

            _recentRequests.Enqueue(now);
            if (_providerRemaining is { } remaining)
            {
                _providerRemaining = Math.Max(0, remaining - 1);
            }

            return decision;
        }
    }

    public void ObserveProviderWindow(
        int? limit,
        int? remaining,
        DateTimeOffset? resetUtc,
        DateTimeOffset now)
    {
        lock (_gate)
        {
            Refresh(now);
            if (limit is > 0)
            {
                _providerLimit = limit;
            }

            if (remaining is { } observedRemaining && observedRemaining >= 0)
            {
                _providerRemaining = _providerLimit is { } observedLimit
                    ? Math.Min(observedRemaining, observedLimit)
                    : observedRemaining;
            }

            if (resetUtc is { } observedReset && observedReset > now)
            {
                _providerResetUtc = observedReset;
            }
            else if (remaining is not null && _providerResetUtc is null)
            {
                // OpenXBL publishes an hourly window. When it omits a reset
                // header, waiting a full hour is safer than speculative retry.
                _providerResetUtc = now + WindowLength;
            }
        }
    }

    public void ObserveRateLimited(DateTimeOffset now, TimeSpan? retryAfter)
    {
        lock (_gate)
        {
            Refresh(now);
            _providerRemaining = 0;
            if (retryAfter is { } supplied && supplied > TimeSpan.Zero)
            {
                _providerResetUtc = now + supplied;
            }
            else if (_providerResetUtc is not { } providerReset || providerReset <= now)
            {
                _providerResetUtc = now + WindowLength;
            }
        }
    }

    private OpenXblRequestDecision Evaluate(
        OpenXblRequestPriority priority,
        int requestedCapacity,
        DateTimeOffset now)
    {
        var retryAt = now;
        var blocked = false;
        var localCeiling = GetLocalCeiling();
        if (_recentRequests.Count + requestedCapacity > localCeiling)
        {
            blocked = true;
            retryAt = Max(
                retryAt,
                _recentRequests.Count > 0
                    ? _recentRequests.Peek() + WindowLength
                    : now + WindowLength);
        }

        if (_providerRemaining is { } remaining)
        {
            var reserve = priority == OpenXblRequestPriority.Background
                ? GetBackgroundReserve()
                : GetEssentialReserve();
            if (remaining < reserve + requestedCapacity)
            {
                blocked = true;
                retryAt = Max(retryAt, _providerResetUtc ?? now + WindowLength);
            }
        }

        if (!blocked)
        {
            return new OpenXblRequestDecision(true, TimeSpan.Zero);
        }

        var retryAfter = retryAt - now;
        return new OpenXblRequestDecision(
            false,
            retryAfter > MinimumRetry ? retryAfter : MinimumRetry);
    }

    private int GetLocalCeiling()
    {
        if (_providerLimit is not { } limit)
        {
            return LocalHourlySafetyCeiling;
        }

        return Math.Max(1, Math.Min(LocalHourlySafetyCeiling, limit - GetEssentialReserve()));
    }

    private int GetEssentialReserve()
    {
        if (_providerLimit is not { } limit)
        {
            return 10;
        }

        return Math.Clamp((int)Math.Ceiling(limit * 0.07), 5, 10);
    }

    private int GetBackgroundReserve()
    {
        if (_providerLimit is not { } limit)
        {
            return 50;
        }

        return Math.Max(
            GetEssentialReserve() + 10,
            Math.Min(50, (int)Math.Ceiling(limit / 3d)));
    }

    private void Refresh(DateTimeOffset now)
    {
        while (_recentRequests.TryPeek(out var timestamp) && now - timestamp >= WindowLength)
        {
            _recentRequests.Dequeue();
        }

        if (_providerResetUtc is { } providerReset && providerReset <= now)
        {
            _providerRemaining = null;
            _providerResetUtc = null;
        }
    }

    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) =>
        left >= right ? left : right;
}
