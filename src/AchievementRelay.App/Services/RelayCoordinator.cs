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
    EventLedger eventLedger,
    DiscordWebhookClient webhookClient,
    ActivityLog activityLog) : IDisposable
{
    private static readonly TimeSpan DeliveryRetryOverlap = TimeSpan.FromHours(24);
    private static readonly TimeSpan FutureClockTolerance = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly SemaphoreSlim _relayGate = new(1, 1);
    private readonly object _lifecycleGate = new();
    private readonly object _statusGate = new();
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _pollingTask;
    private bool _started;
    private DateTimeOffset? _lastSuccessfulSync;
    private string? _lastSyncError;

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
        lock (_statusGate)
        {
            _lastSuccessfulSync = state.LastSuccessfulPollUtc;
            _lastSyncError = null;
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
            _relayGate.Dispose();
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
                interval = retryAfter > TimeSpan.FromMinutes(15) ? TimeSpan.FromMinutes(15) : retryAfter;
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
            var currentSnapshots = XboxSyncStateStore.CreateTitleSnapshots(progressFetch.Titles);
            if (!string.Equals(state.AccountXuid, accountXuid, StringComparison.Ordinal) ||
                state.BaselineUtc is null ||
                state.LastSuccessfulPollUtc is null)
            {
                await syncStateStore.SaveAsync(new XboxSyncState
                {
                    AccountXuid = accountXuid,
                    BaselineUtc = now,
                    LastSuccessfulPollUtc = now,
                    Titles = currentSnapshots
                }, cancellationToken);
                const string baselineMessage = "Xbox baseline established. Achievements earned before setup will not be reposted.";
                SetSuccess(now);
                if (manual)
                {
                    activityLog.Success(baselineMessage);
                }

                return new XboxSyncOutcome(true, baselineMessage, BaselineEstablished: true);
            }

            var overlapStart = state.LastSuccessfulPollUtc.Value - DeliveryRetryOverlap;
            var cutoff = overlapStart > state.BaselineUtc.Value ? overlapStart : state.BaselineUtc.Value;
            var changedTitles = progressFetch.Titles
                .Where(title => HasProgressChanged(title, state.Titles))
                .OrderByDescending(title => title.LastPlayedAt)
                .ThenBy(title => title.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var posted = 0;
            foreach (var title in changedTitles)
            {
                var detailFetch = await openXblClient.GetTitleAchievementsAsync(
                    normalizedApiKey,
                    accountXuid,
                    title.TitleId,
                    title.CurrentAchievements,
                    cancellationToken);
                if (!detailFetch.Success || detailFetch.Achievements is null)
                {
                    return SetError(detailFetch.Message, manual, detailFetch.RetryAfter);
                }

                var previousCount = state.Titles.TryGetValue(title.TitleId, out var previous)
                    ? previous.CurrentAchievements
                    : 0;
                if (title.CurrentAchievements > previousCount &&
                    detailFetch.Achievements.Count < title.CurrentAchievements)
                {
                    return SetError(
                        $"OpenXBL reported new progress for {title.Name ?? "an Xbox title"}, but its achievement details have not caught up yet. Achievement Relay will retry without advancing the sync position.",
                        manual,
                        detailFetch.RetryAfter);
                }

                var candidates = detailFetch.Achievements
                    .Where(achievement =>
                        achievement.UnlockedAt > cutoff &&
                        achievement.UnlockedAt <= now + FutureClockTolerance)
                    .Select(achievement => string.IsNullOrWhiteSpace(achievement.GameName)
                        ? achievement with { GameName = title.Name }
                        : achievement)
                    .OrderBy(achievement => achievement.UnlockedAt)
                    .ToArray();

                if (title.CurrentAchievements > previousCount && candidates.Length == 0)
                {
                    return SetError(
                        $"OpenXBL reported a new unlock for {title.Name ?? "an Xbox title"}, but no new timestamped achievement is available yet. Achievement Relay will retry without advancing the sync position.",
                        manual,
                        detailFetch.RetryAfter);
                }

                foreach (var achievement in candidates)
                {
                    var handling = await ProcessAsync(achievement, settings, cancellationToken);
                    if (handling == AchievementHandlingResult.RetryRequired)
                    {
                        return SetError(
                            $"Discord delivery is pending for {achievement.Name}; the relay will retry automatically.",
                            manual);
                    }

                    if (handling == AchievementHandlingResult.Posted)
                    {
                        posted++;
                    }
                }
            }

            await syncStateStore.SaveAsync(state with
            {
                LastSuccessfulPollUtc = now,
                Titles = currentSnapshots
            }, cancellationToken);
            SetSuccess(now);

            var message = posted == 0
                ? "Xbox account is up to date. No new achievements were found."
                : $"Posted {posted} new Xbox achievement{(posted == 1 ? string.Empty : "s")} to Discord.";
            if (manual)
            {
                activityLog.Success(message);
            }

            return new XboxSyncOutcome(true, message, posted);
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private async Task<AchievementHandlingResult> ProcessAsync(
        AchievementEvent achievement,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        await _relayGate.WaitAsync(cancellationToken);
        try
        {
            if (await eventLedger.ContainsAsync(achievement.Id, cancellationToken))
            {
                return AchievementHandlingResult.Handled;
            }

            if (settings.PostRareOnly && !achievement.IsRare)
            {
                await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
                activityLog.Info($"Skipped common achievement because Rare Only is enabled: {achievement.Name}.");
                return AchievementHandlingResult.Handled;
            }

            var webhookValue = secretProtector.TryUnprotect(settings.ProtectedWebhookUrl);
            if (!WebhookUrlValidator.TryNormalize(webhookValue, out var webhookUri, out _) || webhookUri is null)
            {
                activityLog.Warning($"Found {achievement.Name}, but Discord is not configured.");
                return AchievementHandlingResult.RetryRequired;
            }

            activityLog.Info($"Xbox achievement detected: {achievement.Name}.");
            var payload = DiscordWebhookPayloadFactory.Create(achievement, settings);
            var result = await SendWithRetryAsync(webhookUri, payload, cancellationToken);
            if (!result.Success)
            {
                activityLog.Error($"Could not relay {achievement.Name}: {result.Message}");
                return AchievementHandlingResult.RetryRequired;
            }

            await eventLedger.MarkProcessedAsync(achievement.Id, cancellationToken);
            activityLog.Success($"Posted {achievement.Name} to Discord.");
            return AchievementHandlingResult.Posted;
        }
        finally
        {
            _relayGate.Release();
        }
    }

    private async Task<RelayResult> SendWithRetryAsync(
        Uri webhookUri,
        string payload,
        CancellationToken cancellationToken)
    {
        var delays = new[] { TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(12) };
        RelayResult result = RelayResult.Fail("Delivery did not start.");

        foreach (var delay in delays)
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            result = await webhookClient.SendAsync(webhookUri, payload, cancellationToken);
            if (result.Success || result.StatusCode is >= 400 and < 500 and not 429)
            {
                break;
            }
        }

        return result;
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
        }

        RaiseStatusChanged();
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private static bool HasProgressChanged(
        XboxTitleProgress title,
        IReadOnlyDictionary<string, XboxTitleSnapshot> previousTitles)
    {
        if (!previousTitles.TryGetValue(title.TitleId, out var previous))
        {
            return title.CurrentAchievements > 0 || title.CurrentGamerscore > 0;
        }

        return title.CurrentAchievements != previous.CurrentAchievements ||
               title.CurrentGamerscore != previous.CurrentGamerscore;
    }

    private enum AchievementHandlingResult
    {
        Handled,
        Posted,
        RetryRequired
    }
}
