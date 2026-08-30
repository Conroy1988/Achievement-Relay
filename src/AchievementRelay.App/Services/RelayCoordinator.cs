using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed record XboxSyncOutcome(
    bool Success,
    string Message,
    int NewAchievements = 0,
    bool BaselineEstablished = false,
    TimeSpan? RetryAfter = null);

public sealed class RelayCoordinator(
    OpenXblClient openXblClient,
    SettingsStore settingsStore,
    SecureWebhookProtector secretProtector,
    XboxSyncStateStore syncStateStore,
    AchievementDeliveryService deliveryService,
    ActivityLog activityLog) : IDisposable
{
    private static readonly TimeSpan FutureClockTolerance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan BackgroundWorkInterval = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MaximumProviderBackoff = TimeSpan.FromHours(2);

    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly object _lifecycleGate = new();
    private readonly object _statusGate = new();
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _pollingTask;
    private bool _started;
    private DateTimeOffset? _lastSuccessfulSync;
    private string? _lastSyncError;
    private DateTimeOffset? _deliveryEpochUtc;
    private DateTimeOffset? _lastSessionSuccessfulPollUtc;

    public event EventHandler? StatusChanged;

    public bool IsRunning
    {
        get
        {
            lock (_lifecycleGate)
            {
                return _started;
            }
        }
    }

    public DateTimeOffset? LastSuccessfulSync
    {
        get
        {
            lock (_statusGate)
            {
                return _lastSuccessfulSync;
            }
        }
    }

    public string? LastSyncError
    {
        get
        {
            lock (_statusGate)
            {
                return _lastSyncError;
            }
        }
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_lifecycleGate)
        {
            if (_started)
            {
                return true;
            }
        }

        var settings = await settingsStore.LoadAsync(cancellationToken);
        var apiKey = secretProtector.TryUnprotectOpenXblApiKey(settings.ProtectedOpenXblApiKey);
        if (!OpenXblApiKeyValidator.TryNormalize(apiKey, out _, out _))
        {
            SetError("Connect an Xbox account through OpenXBL before starting the relay.", log: false);
            return false;
        }

        var state = await syncStateStore.LoadAsync(cancellationToken);
        var sessionStartedUtc = DateTimeOffset.UtcNow;
        lock (_statusGate)
        {
            _lastSuccessfulSync = state.LastSuccessfulPollUtc;
            _lastSyncError = null;
            _deliveryEpochUtc = sessionStartedUtc;
            _lastSessionSuccessfulPollUtc = null;
        }

        lock (_lifecycleGate)
        {
            if (_started)
            {
                return true;
            }

            var lifetimeCancellation = new CancellationTokenSource();
            _lifetimeCancellation = lifetimeCancellation;
            _started = true;
            _pollingTask = Task.Run(() => RunPollingLoopAsync(lifetimeCancellation.Token));
        }

        activityLog.Success("Xbox account monitoring is active through OpenXBL.");
        RaiseStatusChanged();
        return true;
    }

    public async Task<XboxSyncOutcome> SyncNowAsync(CancellationToken cancellationToken = default) =>
        await SyncOnceAsync(manual: true, cancellationToken);

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? pollingTask;
        bool wasStarted;
        lock (_lifecycleGate)
        {
            wasStarted = _started;
            if (!_started && _pollingTask is null)
            {
                return;
            }

            _started = false;
            cancellation = _lifetimeCancellation;
            pollingTask = _pollingTask;
            _lifetimeCancellation = null;
            _pollingTask = null;
        }

        cancellation?.Cancel();
        try
        {
            if (pollingTask is not null)
            {
                await pollingTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal when the background poll is stopped.
        }
        catch (Exception)
        {
            activityLog.Warning("Xbox account monitoring stopped after an unexpected background error.");
        }
        finally
        {
            cancellation?.Dispose();
        }

        if (wasStarted)
        {
            lock (_statusGate)
            {
                _deliveryEpochUtc = null;
                _lastSessionSuccessfulPollUtc = null;
            }

            activityLog.Info("Xbox account monitoring stopped.");
            RaiseStatusChanged();
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _syncGate.Dispose();
        }
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            XboxSyncOutcome outcome;
            try
            {
                outcome = await SyncOnceAsync(manual: false, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                outcome = SetError(
                    "Xbox account sync stopped unexpectedly during this check. Achievement Relay will retry automatically.",
                    log: false);
            }

            var settings = await settingsStore.LoadAsync(cancellationToken);
            var interval = TimeSpan.FromSeconds(Math.Clamp(settings.PollIntervalSeconds, 60, 3600));
            if (outcome.RetryAfter is { } retryAfter && retryAfter > interval)
            {
                interval = retryAfter > MaximumProviderBackoff ? MaximumProviderBackoff : retryAfter;
            }

            await Task.Delay(interval, cancellationToken);
        }
    }

    private async Task<XboxSyncOutcome> SyncOnceAsync(bool manual, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var settings = await settingsStore.LoadAsync(cancellationToken);
            var apiKey = secretProtector.TryUnprotectOpenXblApiKey(settings.ProtectedOpenXblApiKey);
            if (!OpenXblApiKeyValidator.TryNormalize(apiKey, out var normalizedApiKey, out var keyError))
            {
                return SetError(keyError ?? "OpenXBL is not configured.", manual);
            }

            var accountXuid = settings.XboxUserId;
            if (string.IsNullOrWhiteSpace(accountXuid))
            {
                var accountResult = await openXblClient.GetAccountAsync(normalizedApiKey, cancellationToken);
                if (!accountResult.Success || accountResult.Account is null)
                {
                    return SetError(accountResult.Message, manual, accountResult.RetryAfter);
                }

                accountXuid = accountResult.Account.Xuid;
                settings = settings with
                {
                    XboxUserId = accountResult.Account.Xuid,
                    XboxGamertag = accountResult.Account.Gamertag
                };
                await settingsStore.SaveAsync(settings, cancellationToken);
            }

            var progressFetch = await openXblClient.GetTitleProgressAsync(
                normalizedApiKey,
                accountXuid,
                cancellationToken);
            if (!progressFetch.Success || progressFetch.Titles is null)
            {
                return SetError(progressFetch.Message, manual, progressFetch.RetryAfter);
            }

            var now = DateTimeOffset.UtcNow;
            var state = await syncStateStore.LoadAsync(cancellationToken);
            if (!string.Equals(state.AccountXuid, accountXuid, StringComparison.Ordinal) ||
                state.BaselineUtc is null ||
                state.LastSuccessfulPollUtc is null)
            {
                var baselineSnapshots = XboxSyncStateStore.CreateTitleSnapshots(progressFetch.Titles);
                await syncStateStore.SaveAsync(new XboxSyncState
                {
                    AccountXuid = accountXuid,
                    BaselineUtc = now,
                    LastSuccessfulPollUtc = now,
                    Titles = baselineSnapshots
                }, cancellationToken);
                const string baselineMessage = "Xbox baseline established. Achievements earned before setup will not be reposted.";
                SetSuccess(now);
                if (manual)
                {
                    activityLog.Success(baselineMessage);
                }

                return new XboxSyncOutcome(true, baselineMessage, BaselineEstablished: true);
            }

            var deliveryWindow = ResolveDeliveryWindow(now, settings.PollIntervalSeconds);
            if (deliveryWindow.ReconciledAfterGap)
            {
                activityLog.Info(
                    "Xbox monitoring resumed after an interruption. Achievements unlocked while this device was inactive will be baselined silently to prevent cross-device reposts.");
            }

            var visibleTitles = progressFetch.Titles
                .GroupBy(title => title.TitleId, StringComparer.Ordinal)
                .Select(group =>
                {
                    var selected = group
                        .OrderByDescending(title => title.CurrentAchievements)
                        .ThenByDescending(title => title.CurrentGamerscore)
                        .ThenByDescending(title => title.LastPlayedAt)
                        .First();
                    return selected with
                    {
                        Devices = XboxPlatformClassifier.NormalizeDevices(
                            group.SelectMany(title => title.Devices)),
                        DisplayImageUrl = group
                            .Select(title => title.DisplayImageUrl)
                            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                    };
                })
                .ToArray();
            var currentSnapshots = state.Titles.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);
            var pendingTitles = state.PendingTitles.ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                StringComparer.Ordinal);

            // The title index is cheap; title detail may require route probes
            // and continuation pages. Queue all changes durably, but expand at
            // most one title in this sync and never advance an unprocessed
            // count. That prevents a newly revealed history page from causing
            // either a Discord flood or an OpenXBL request burst.
            foreach (var title in visibleTitles)
            {
                if (HasProgressChanged(title, currentSnapshots))
                {
                    QueueTitleWork(
                        title,
                        state,
                        currentSnapshots,
                        pendingTitles,
                        deliveryWindow,
                        now);
                }
                else if (!currentSnapshots.ContainsKey(title.TitleId))
                {
                    currentSnapshots[title.TitleId] = new XboxTitleSnapshot
                    {
                        CurrentAchievements = Math.Max(0, title.CurrentAchievements),
                        CurrentGamerscore = Math.Max(0, title.CurrentGamerscore),
                        Devices = XboxPlatformClassifier.NormalizeDevices(title.Devices),
                        DisplayImageUrl = NormalizeUrlHint(title.DisplayImageUrl)
                    };
                }
            }

            foreach (var pending in pendingTitles.ToArray())
            {
                if (IsPendingWorkSatisfied(pending.Value, currentSnapshots))
                {
                    pendingTitles.Remove(pending.Key);
                }
            }

            var lastBackgroundWorkUtc = state.LastBackgroundWorkUtc;
            var backgroundWorkDue = !manual && XboxSyncWorkPlanner.IsBackgroundWorkDue(
                lastBackgroundWorkUtc,
                now,
                BackgroundWorkInterval);
            var selectedWork = XboxSyncWorkPlanner.SelectNext(pendingTitles.Values, backgroundWorkDue);
            var posted = 0;
            var safelyBaselined = 0;
            TimeSpan? backgroundRetryAfter = null;

            async Task SaveProgressAsync(DateTimeOffset? successfulPollUtc)
            {
                await syncStateStore.SaveAsync(state with
                {
                    LastSuccessfulPollUtc = successfulPollUtc,
                    LastBackgroundWorkUtc = lastBackgroundWorkUtc,
                    Titles = currentSnapshots,
                    PendingTitles = pendingTitles
                }, cancellationToken);
            }

            if (selectedWork is not null)
            {
                var isBackgroundWork = !selectedWork.IsPriority;
                var detailFetch = await openXblClient.GetTitleAchievementsAsync(
                    normalizedApiKey,
                    accountXuid,
                    selectedWork.TitleId,
                    selectedWork.CurrentAchievements,
                    isBackgroundWork ? OpenXblRequestPriority.Background : OpenXblRequestPriority.Essential,
                    cancellationToken);

                if (isBackgroundWork)
                {
                    lastBackgroundWorkUtc = now;
                    backgroundRetryAfter = ShouldPauseAllOpenXblWork(detailFetch)
                        ? detailFetch.RetryAfter
                        : null;
                }

                if (!detailFetch.Success || detailFetch.Achievements is null)
                {
                    await SaveProgressAsync(isBackgroundWork ? now : state.LastSuccessfulPollUtc);
                    if (isBackgroundWork)
                    {
                        SetSuccess(now);
                        return new XboxSyncOutcome(
                            true,
                            "Xbox monitoring is active. Historical identity baselining was safely deferred to protect the OpenXBL allowance.",
                            RetryAfter: backgroundRetryAfter);
                    }

                    return SetError(detailFetch.Message, manual, detailFetch.RetryAfter);
                }

                var hadPreviousSnapshot = currentSnapshots.TryGetValue(selectedWork.TitleId, out var previous);
                var previousCount = hadPreviousSnapshot ? previous!.CurrentAchievements : 0;
                var effectiveDeliveryEpoch = selectedWork.LiveDeliveryEpochUtc ?? deliveryWindow.EpochUtc;
                var delta = AchievementDeltaDetector.Detect(
                    previousCount,
                    hadPreviousSnapshot ? previous!.UnlockedAchievementIds : null,
                    selectedWork.CurrentAchievements,
                    detailFetch.Achievements,
                    effectiveDeliveryEpoch,
                    now,
                    FutureClockTolerance,
                    allowUntimestampedIdentityDelta: selectedWork.AllowsUntimestampedDelivery);
                if (!delta.IsComplete)
                {
                    await SaveProgressAsync(isBackgroundWork ? now : state.LastSuccessfulPollUtc);
                    var incompleteMessage =
                        $"OpenXBL reported progress for {selectedWork.Name ?? "an Xbox title"}, but its achievement details have not caught up yet. Achievement Relay will retry without advancing the sync position.";
                    if (isBackgroundWork)
                    {
                        SetSuccess(now);
                        return new XboxSyncOutcome(
                            true,
                            "Xbox monitoring is active. An older title remains queued until OpenXBL returns its complete identity list.",
                            RetryAfter: backgroundRetryAfter);
                    }

                    return SetError(incompleteMessage, manual, detailFetch.RetryAfter);
                }

                if (delta.UnidentifiedIncrease > 0)
                {
                    safelyBaselined += delta.UnidentifiedIncrease;
                    activityLog.Info(
                        $"Silently baselined {delta.UnidentifiedIncrease} existing achievement{(delta.UnidentifiedIncrease == 1 ? "" : "s")} for {selectedWork.Name ?? "an Xbox title"}. Nothing historical was sent to Discord; only later unlocks are eligible.");
                }

                foreach (var achievement in delta.NewAchievements.Select(item =>
                             PrepareForDelivery(
                                 item,
                                 selectedWork.Name,
                                 selectedWork.Devices,
                                 selectedWork.DisplayImageUrl,
                                 now)))
                {
                    var handling = await deliveryService.DeliverAsync(achievement, settings, cancellationToken);
                    if (handling == AchievementDeliveryResult.RetryRequired)
                    {
                        await SaveProgressAsync(state.LastSuccessfulPollUtc);
                        return SetError(
                            $"Discord delivery is pending for {achievement.Name}; the relay will retry automatically.",
                            manual);
                    }

                    if (handling == AchievementDeliveryResult.Posted)
                    {
                        posted++;
                    }
                }

                currentSnapshots[selectedWork.TitleId] = new XboxTitleSnapshot
                {
                    CurrentAchievements = Math.Max(selectedWork.CurrentAchievements, previousCount),
                    CurrentGamerscore = Math.Max(
                        selectedWork.CurrentGamerscore,
                        hadPreviousSnapshot ? previous!.CurrentGamerscore : 0),
                    Devices = XboxPlatformClassifier.NormalizeDevices(
                        selectedWork.Devices,
                        previous?.Devices),
                    DisplayImageUrl = FirstUrlHint(
                        selectedWork.DisplayImageUrl,
                        previous?.DisplayImageUrl),
                    UnlockedAchievementIds = delta.CurrentAchievementIds.ToArray()
                };
                pendingTitles.Remove(selectedWork.TitleId);
            }

            // Count-only snapshots hydrate in the same fifteen-minute
            // background slot, never from a manual Sync Now click. A priority
            // achievement change always wins this slot.
            if (selectedWork is null && backgroundWorkDue)
            {
                var hydrationTitle = visibleTitles
                    .Where(title => !pendingTitles.ContainsKey(title.TitleId) &&
                                    currentSnapshots.TryGetValue(title.TitleId, out var snapshot) &&
                                    !snapshot.HasAchievementIdentityBaseline &&
                                    !HasProgressChanged(title, currentSnapshots))
                    .OrderByDescending(title => title.LastPlayedAt)
                    .ThenBy(title => title.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (hydrationTitle is not null)
                {
                    lastBackgroundWorkUtc = now;
                    var hydrationFetch = await openXblClient.GetTitleAchievementsAsync(
                        normalizedApiKey,
                        accountXuid,
                        hydrationTitle.TitleId,
                        hydrationTitle.CurrentAchievements,
                        OpenXblRequestPriority.Background,
                        cancellationToken);
                    backgroundRetryAfter = ShouldPauseAllOpenXblWork(hydrationFetch)
                        ? hydrationFetch.RetryAfter
                        : null;
                    if (hydrationFetch.Success && hydrationFetch.Achievements is not null)
                    {
                        var hydrationDelta = AchievementDeltaDetector.Detect(
                            hydrationTitle.CurrentAchievements,
                            null,
                            hydrationTitle.CurrentAchievements,
                            hydrationFetch.Achievements,
                            deliveryWindow.EpochUtc,
                            now,
                            FutureClockTolerance,
                            allowUntimestampedIdentityDelta: false);
                        if (hydrationDelta.IsComplete)
                        {
                            currentSnapshots[hydrationTitle.TitleId] = currentSnapshots[hydrationTitle.TitleId] with
                            {
                                UnlockedAchievementIds = hydrationDelta.CurrentAchievementIds.ToArray()
                            };
                        }
                    }
                }
            }

            await SaveProgressAsync(now);
            SetSuccess(now);

            var message = posted > 0
                ? $"Posted {posted} new Xbox achievement{(posted == 1 ? string.Empty : "s")} to Discord."
                : safelyBaselined > 0
                    ? "Existing achievement history was baselined silently. Monitoring will post only later unlocks."
                    : pendingTitles.Count > 0
                        ? $"Xbox monitoring is active. {pendingTitles.Count} older title{(pendingTitles.Count == 1 ? "" : "s")} remain queued for gradual, silent identity baselining."
                        : "Xbox account is up to date. No new achievements were found.";
            if (manual)
            {
                activityLog.Success(message);
            }

            return new XboxSyncOutcome(true, message, posted, RetryAfter: backgroundRetryAfter);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private XboxSyncOutcome SetError(string message, bool log, TimeSpan? retryAfter = null)
    {
        bool changed;
        lock (_statusGate)
        {
            changed = !string.Equals(_lastSyncError, message, StringComparison.Ordinal);
            _lastSyncError = message;
        }

        if (log || changed)
        {
            activityLog.Warning(message);
        }

        RaiseStatusChanged();
        return new XboxSyncOutcome(false, message, RetryAfter: retryAfter);
    }

    private void SetSuccess(DateTimeOffset timestamp)
    {
        lock (_statusGate)
        {
            _lastSuccessfulSync = timestamp;
            _lastSyncError = null;
            _lastSessionSuccessfulPollUtc = timestamp;
        }

        RaiseStatusChanged();
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private XboxDeliveryWindowDecision ResolveDeliveryWindow(
        DateTimeOffset observedAt,
        int pollIntervalSeconds)
    {
        lock (_statusGate)
        {
            var decision = XboxDeliveryWindowPolicy.Resolve(
                _deliveryEpochUtc,
                _lastSessionSuccessfulPollUtc,
                observedAt,
                pollIntervalSeconds);
            _deliveryEpochUtc = decision.EpochUtc;
            if (decision.ReconciledAfterGap)
            {
                _lastSessionSuccessfulPollUtc = null;
            }

            return decision;
        }
    }

    private static void QueueTitleWork(
        XboxTitleProgress title,
        XboxSyncState state,
        IReadOnlyDictionary<string, XboxTitleSnapshot> currentSnapshots,
        IDictionary<string, XboxTitleSyncWork> pendingTitles,
        XboxDeliveryWindowDecision deliveryWindow,
        DateTimeOffset observedAt)
    {
        pendingTitles.TryGetValue(title.TitleId, out var existing);
        currentSnapshots.TryGetValue(title.TitleId, out var previous);
        var playedAfterMonitoringBegan = title.LastPlayedAt is { } lastPlayedAt &&
                                         state.BaselineUtc is { } baselineUtc &&
                                         lastPlayedAt > baselineUtc;
        var changedKnownSummary = previous is not null &&
                                  (title.CurrentAchievements > previous.CurrentAchievements ||
                                   title.CurrentGamerscore > previous.CurrentGamerscore);
        var increasedWhileQueued = existing is not null &&
                                   (title.CurrentAchievements > existing.CurrentAchievements ||
                                    title.CurrentGamerscore > existing.CurrentGamerscore);
        var firstObserved = existing?.FirstObservedUtc is { } existingFirst &&
                            existingFirst != default
            ? existingFirst
            : observedAt;
        var newlyObservedAfterSuccessfulPoll = existing is null && deliveryWindow.HasPriorSuccessfulPoll;
        var hasUntimestampedLiveEvidence = newlyObservedAfterSuccessfulPoll &&
                                           previous?.HasAchievementIdentityBaseline == true;

        pendingTitles[title.TitleId] = new XboxTitleSyncWork
        {
            TitleId = title.TitleId,
            Name = string.IsNullOrWhiteSpace(title.Name) ? existing?.Name : title.Name,
            CurrentAchievements = Math.Max(
                Math.Max(0, title.CurrentAchievements),
                existing?.CurrentAchievements ?? 0),
            CurrentGamerscore = Math.Max(
                Math.Max(0, title.CurrentGamerscore),
                existing?.CurrentGamerscore ?? 0),
            LastPlayedAt = Max(existing?.LastPlayedAt, title.LastPlayedAt),
            Devices = XboxPlatformClassifier.NormalizeDevices(
                title.Devices,
                existing?.Devices,
                previous?.Devices),
            DisplayImageUrl = FirstUrlHint(
                title.DisplayImageUrl,
                existing?.DisplayImageUrl,
                previous?.DisplayImageUrl),
            FirstObservedUtc = firstObserved,
            LastObservedUtc = observedAt,
            LiveDeliveryEpochUtc = existing?.LiveDeliveryEpochUtc ??
                                   (newlyObservedAfterSuccessfulPoll
                                       ? deliveryWindow.EpochUtc
                                       : null),
            AllowsUntimestampedDelivery = existing?.AllowsUntimestampedDelivery == true ||
                                          hasUntimestampedLiveEvidence,
            IsPriority = existing?.IsPriority == true ||
                         previous?.HasAchievementIdentityBaseline == true ||
                         changedKnownSummary ||
                         increasedWhileQueued ||
                         playedAfterMonitoringBegan
        };
    }

    private static bool IsPendingWorkSatisfied(
        XboxTitleSyncWork work,
        IReadOnlyDictionary<string, XboxTitleSnapshot> currentSnapshots) =>
        currentSnapshots.TryGetValue(work.TitleId, out var snapshot) &&
        snapshot.HasAchievementIdentityBaseline &&
        snapshot.CurrentAchievements >= work.CurrentAchievements &&
        snapshot.CurrentGamerscore >= work.CurrentGamerscore;

    private static bool ShouldPauseAllOpenXblWork(OpenXblAchievementsResult result) =>
        result.AllowanceProtected || result.StatusCode == 429;

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private static bool HasProgressChanged(
        XboxTitleProgress title,
        IReadOnlyDictionary<string, XboxTitleSnapshot> previousTitles)
    {
        if (!previousTitles.TryGetValue(title.TitleId, out var previous))
        {
            return title.CurrentAchievements > 0 || title.CurrentGamerscore > 0;
        }

        return title.CurrentAchievements > previous.CurrentAchievements ||
               title.CurrentGamerscore > previous.CurrentGamerscore;
    }

    private static AchievementEvent PrepareForDelivery(
        AchievementEvent achievement,
        string? fallbackGameName,
        IEnumerable<string>? titleDevices,
        string? fallbackHeroImageUrl,
        DateTimeOffset observedAt)
    {
        var reportedTimeIsUsable = achievement.UnlockedAt is { } unlockedAt &&
                                   unlockedAt <= observedAt + FutureClockTolerance;
        return achievement with
        {
            GameName = string.IsNullOrWhiteSpace(achievement.GameName)
                ? fallbackGameName
                : achievement.GameName,
            Platform = XboxPlatformClassifier.ForDelivery(
                achievement.Platform,
                titleDevices),
            HeroImageUrl = string.IsNullOrWhiteSpace(achievement.HeroImageUrl)
                ? NormalizeUrlHint(fallbackHeroImageUrl)
                : achievement.HeroImageUrl,
            UnlockedAt = reportedTimeIsUsable ? achievement.UnlockedAt : observedAt,
            UnlockTimeEstimated = achievement.UnlockTimeEstimated || !reportedTimeIsUsable
        };
    }

    private static string? NormalizeUrlHint(string? value)
    {
        const int maximumLength = 2048;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : null;
    }

    private static string? FirstUrlHint(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalized = NormalizeUrlHint(value);
            if (normalized is not null)
            {
                return normalized;
            }
        }

        return null;
    }

}
