using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public enum SteamMonitoringPhase
{
    WaitingForGame,
    Connecting,
    LoadingStats,
    EstablishingBaseline,
    Monitoring,
    Retrying
}

public sealed class SteamMonitorCoordinator(
    SteamGameDetector gameDetector,
    SteamSyncStateStore stateStore,
    SteamRarityClient rarityClient,
    SettingsStore settingsStore,
    AchievementDeliveryService deliveryService,
    ActivityLog activityLog) : IDisposable
{
    private const int ProtocolVersion = 1;
    private static readonly TimeSpan FutureClockTolerance = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GameExitGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan BridgeRestartDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InitialObservationTimeout = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _lifecycleGate = new();
    private readonly object _statusGate = new();
    private readonly SemaphoreSlim _bridgeGate = new(1, 1);
    private readonly SemaphoreSlim _snapshotGate = new(1, 1);
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _monitorTask;
    private Process? _bridgeProcess;
    private Task? _bridgeReaderTask;
    private Task? _bridgeErrorTask;
    private SteamGameInfo? _currentGame;
    private DateTimeOffset _gameDetectedUtc;
    private DateTimeOffset? _gameMissingSinceUtc;
    private DateTimeOffset _nextBridgeStartUtc;
    private bool _started;
    private string? _steamPlayerName;
    private string? _lastError;
    private DateTimeOffset? _lastObservationUtc;
    private DateTimeOffset? _bridgeStartedUtc;
    private bool _bridgeHasObservation;
    private SteamMonitoringPhase _phase = SteamMonitoringPhase.WaitingForGame;
    private DateTimeOffset _nextDeliveryAttemptUtc;
    private int _consecutiveDeliveryFailures;

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

    public bool IsSteamInstalled => gameDetector.IsSteamInstalled;

    public bool IsSteamRunning => gameDetector.IsSteamRunning;

    public bool IsSupportedPlatform =>
        RuntimeInformation.OSArchitecture != Architecture.Arm64 ||
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public string? CurrentGameName
    {
        get
        {
            lock (_statusGate)
            {
                return _currentGame?.Name;
            }
        }
    }

    public string? SteamPlayerName
    {
        get
        {
            lock (_statusGate)
            {
                return _steamPlayerName;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_statusGate)
            {
                return _lastError;
            }
        }
    }

    public DateTimeOffset? LastObservationUtc
    {
        get
        {
            lock (_statusGate)
            {
                return _lastObservationUtc;
            }
        }
    }

    public SteamMonitoringPhase Phase
    {
        get
        {
            lock (_statusGate)
            {
                return _phase;
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
        if (!settings.SteamEnabled)
        {
            return false;
        }

        if (!IsSupportedPlatform)
        {
            SetError("Steam monitoring on Arm64 requires Windows 11 x64 emulation. Xbox monitoring remains available on this Windows version.");
            return false;
        }

        if (!File.Exists(GetBridgePath()))
        {
            SetError("The Steam monitoring component is missing. Reinstall Achievement Relay.");
            return false;
        }

        lock (_lifecycleGate)
        {
            if (_started)
            {
                return true;
            }

            _lifetimeCancellation = new CancellationTokenSource();
            _started = true;
            _monitorTask = Task.Run(() => RunMonitorLoopAsync(_lifetimeCancellation.Token));
        }

        activityLog.Success("Steam monitoring is active. Start a Steam game; its existing achievements will be baselined silently.");
        RaiseStatusChanged();
        return true;
    }

    public async Task<bool> RestartAsync(CancellationToken cancellationToken = default)
    {
        await StopAsync();
        return await StartAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? monitorTask;
        lock (_lifecycleGate)
        {
            if (!_started && _monitorTask is null)
            {
                return;
            }

            _started = false;
            cancellation = _lifetimeCancellation;
            monitorTask = _monitorTask;
            _lifetimeCancellation = null;
            _monitorTask = null;
        }

        cancellation?.Cancel();
        try
        {
            if (monitorTask is not null)
            {
                await monitorTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            await StopBridgeAsync(CancellationToken.None).ConfigureAwait(false);
            cancellation?.Dispose();
            lock (_statusGate)
            {
                _currentGame = null;
                _steamPlayerName = null;
                _gameMissingSinceUtc = null;
                _lastObservationUtc = null;
                _lastError = null;
                _bridgeStartedUtc = null;
                _bridgeHasObservation = false;
                _phase = SteamMonitoringPhase.WaitingForGame;
            }

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
            _bridgeGate.Dispose();
            _snapshotGate.Dispose();
        }
    }

    private async Task RunMonitorLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            SteamGameInfo? current;
            lock (_statusGate)
            {
                current = _currentGame;
            }

            SteamGameInfo? detected = null;
            try
            {
                detected = gameDetector.Detect(current);
            }
            catch (Exception)
            {
                SetError("Steam game detection failed temporarily. Achievement Relay will retry automatically.");
            }

            var now = DateTimeOffset.UtcNow;
            if (detected is not null)
            {
                lock (_statusGate)
                {
                    _gameMissingSinceUtc = null;
                }

                if (current is null || current.AppId != detected.AppId)
                {
                    await StopBridgeAsync(cancellationToken);
                    _consecutiveDeliveryFailures = 0;
                    _nextDeliveryAttemptUtc = DateTimeOffset.MinValue;
                    lock (_statusGate)
                    {
                        _currentGame = detected;
                        _gameDetectedUtc = now;
                        _steamPlayerName = null;
                        _lastObservationUtc = null;
                        _lastError = null;
                        _bridgeStartedUtc = null;
                        _bridgeHasObservation = false;
                        _phase = SteamMonitoringPhase.Connecting;
                    }

                    _nextBridgeStartUtc = DateTimeOffset.MinValue;
                    activityLog.Info($"Steam game detected: {detected.Name}. Establishing a silent achievement baseline.");
                    RaiseStatusChanged();
                }
            }
            else if (current is not null)
            {
                DateTimeOffset missingSince;
                lock (_statusGate)
                {
                    _gameMissingSinceUtc ??= now;
                    missingSince = _gameMissingSinceUtc.Value;
                }

                if (now - missingSince >= GameExitGracePeriod)
                {
                    await StopBridgeAsync(cancellationToken);
                    lock (_statusGate)
                    {
                        _currentGame = null;
                        _steamPlayerName = null;
                        _gameMissingSinceUtc = null;
                        _lastObservationUtc = null;
                        _lastError = null;
                        _bridgeStartedUtc = null;
                        _bridgeHasObservation = false;
                        _phase = SteamMonitoringPhase.WaitingForGame;
                    }

                    activityLog.Info($"Steam game closed: {current.Name}.");
                    RaiseStatusChanged();
                }
            }

            lock (_statusGate)
            {
                current = _currentGame;
            }

            if (current is not null && now >= _nextBridgeStartUtc && !IsBridgeAlive())
            {
                await StopBridgeAsync(cancellationToken);
                await StartBridgeAsync(current, cancellationToken);
                _nextBridgeStartUtc = now + BridgeRestartDelay;
            }

            CheckBridgeStartupTimeout(current, now);

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task<bool> StartBridgeAsync(SteamGameInfo game, CancellationToken cancellationToken)
    {
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            if (IsBridgeAlive())
            {
                return true;
            }

            var bridgePath = GetBridgePath();
            if (!File.Exists(bridgePath))
            {
                SetError("The Steam monitoring component is missing. Reinstall Achievement Relay.");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = bridgePath,
                WorkingDirectory = Path.GetDirectoryName(bridgePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.ArgumentList.Add("--app-id");
            startInfo.ArgumentList.Add(game.AppId.ToString(CultureInfo.InvariantCulture));
            startInfo.ArgumentList.Add("--parent-pid");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            if (!process.Start())
            {
                process.Dispose();
                SetError($"Steam monitoring could not start for {game.Name}. Achievement Relay will retry.");
                return false;
            }

            _bridgeProcess = process;
            lock (_statusGate)
            {
                _bridgeStartedUtc = DateTimeOffset.UtcNow;
                _bridgeHasObservation = false;
                _phase = SteamMonitoringPhase.Connecting;
            }

            _bridgeReaderTask = ReadBridgeOutputAsync(process, cancellationToken);
            _bridgeErrorTask = DrainBridgeErrorsAsync(process, cancellationToken);
            RaiseStatusChanged();
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            SetError($"Steam monitoring could not start for {game.Name}. Achievement Relay will retry.");
            return false;
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    private async Task StopBridgeAsync(CancellationToken cancellationToken)
    {
        await _bridgeGate.WaitAsync(cancellationToken);
        try
        {
            // After ownership is detached, teardown must finish even if the
            // monitor's lifetime token is cancelled. Otherwise the fields are
            // cleared while an orphan helper keeps running behind the tray app.
            var teardownToken = CancellationToken.None;
            var process = _bridgeProcess;
            var reader = _bridgeReaderTask;
            var errors = _bridgeErrorTask;
            _bridgeProcess = null;
            _bridgeReaderTask = null;
            _bridgeErrorTask = null;
            lock (_statusGate)
            {
                _bridgeStartedUtc = null;
                _bridgeHasObservation = false;
            }

            if (process is null)
            {
                return;
            }

            try
            {
                if (!process.HasExited)
                {
                    await process.StandardInput.WriteLineAsync("quit");
                    await process.StandardInput.FlushAsync(teardownToken);
                    var exited = process.WaitForExitAsync(teardownToken);
                    if (await Task.WhenAny(exited, Task.Delay(TimeSpan.FromSeconds(4), teardownToken)) != exited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
            {
                // A bridge that has already exited needs no further shutdown.
            }

            if (reader is not null || errors is not null)
            {
                var pending = new[] { reader, errors }.Where(task => task is not null).Cast<Task>();
                try
                {
                    await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2), teardownToken);
                }
                catch (Exception exception) when (exception is TimeoutException or OperationCanceledException or IOException)
                {
                    // Output drain is best effort during process teardown.
                }
            }

            process.Dispose();
        }
        finally
        {
            _bridgeGate.Release();
        }
    }

    private async Task ReadBridgeOutputAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await process.StandardOutput.ReadLineAsync(cancellationToken) is { } line)
            {
                try
                {
                    await HandleBridgeMessageAsync(line, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (InvalidDataException)
                {
                    SetError("The Steam monitoring component returned unreadable or oversized data. Nothing was posted; Achievement Relay will restart it.");
                    TryTerminateBridge(process);
                    return;
                }
                catch (Exception exception)
                {
                    // Keep draining the trusted helper. It repeats directly
                    // proven transitions on complete heartbeats, so a transient
                    // local-state or delivery failure can recover in place
                    // without losing the observation to a helper restart.
                    SetError($"Steam observation processing failed safely ({exception.GetType().Name}). Nothing unverified was posted; Achievement Relay will retry.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The monitor loop owns restart policy.
        }
        catch (Exception)
        {
            // A pipe-level failure means output can no longer be drained.
            // Terminate only the isolated helper and let the monitor restart it.
            SetError("Steam observation processing stopped safely. Nothing unverified was posted; Achievement Relay will retry.");
            TryTerminateBridge(process);
        }
    }

    private static void TryTerminateBridge(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // The bridge already exited or Windows completed teardown.
        }
    }

    private static async Task DrainBridgeErrorsAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await process.StandardError.ReadLineAsync(cancellationToken) is not null)
            {
                // Intentionally drained but not persisted: native diagnostics can
                // contain local paths, and the structured channel carries errors.
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or OperationCanceledException)
        {
            // Process teardown.
        }
    }

    private async Task HandleBridgeMessageAsync(string line, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        if (line.Length > 8_000_000)
        {
            throw new InvalidDataException("Steam bridge message exceeded the protocol limit.");
        }

        SteamBridgeMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<SteamBridgeMessage>(line, JsonOptions);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("Steam bridge returned unreadable JSON.");
        }

        if (message is null || message.ProtocolVersion != ProtocolVersion)
        {
            SetError("The Steam monitoring component is incompatible with this app version. Reinstall Achievement Relay.");
            return;
        }

        if (string.Equals(message.Type, "status", StringComparison.OrdinalIgnoreCase))
        {
            SteamGameInfo? game;
            lock (_statusGate)
            {
                game = _currentGame;
            }

            if (game is null || (message.AppId != 0 && message.AppId != game.AppId))
            {
                return;
            }

            if (string.Equals(message.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                SetError(string.IsNullOrWhiteSpace(message.Message)
                    ? "The Steam observer could not initialize for this game. Achievement Relay will retry."
                    : message.Message!);
            }
            else if (string.Equals(message.Status, "connected", StringComparison.OrdinalIgnoreCase))
            {
                SetPhase(SteamMonitoringPhase.LoadingStats);
                if (string.IsNullOrWhiteSpace(LastError))
                {
                    activityLog.Info($"Steam observer connected for {game.Name}. Requesting current achievement stats.");
                }
            }
            else if (string.Equals(message.Status, "stats-ready", StringComparison.OrdinalIgnoreCase))
            {
                SetPhase(SteamMonitoringPhase.EstablishingBaseline);
                if (string.IsNullOrWhiteSpace(LastError))
                {
                    activityLog.Info($"Steam achievement stats are ready for {game.Name}. Building a silent baseline.");
                }
            }

            return;
        }

        if (!string.Equals(message.Type, "snapshot", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await ProcessSnapshotAsync(message, cancellationToken);
    }

    private async Task ProcessSnapshotAsync(SteamBridgeMessage snapshot, CancellationToken cancellationToken)
    {
        await _snapshotGate.WaitAsync(cancellationToken);
        var processingStage = "snapshot validation";
        var transitionPersisted = false;
        try
        {
            SteamGameInfo? game;
            DateTimeOffset detectedAt;
            lock (_statusGate)
            {
                game = _currentGame;
                detectedAt = _gameDetectedUtc;
            }

            var snapshotAchievements = snapshot.Achievements;
            if (game is null || game.AppId != snapshot.AppId ||
                string.IsNullOrWhiteSpace(snapshot.SteamId) ||
                !ulong.TryParse(snapshot.SteamId, out var steamIdValue) || steamIdValue == 0 ||
                !snapshot.Complete || snapshotAchievements is null ||
                snapshot.TotalAchievements <= 0 ||
                snapshot.TotalAchievements != snapshotAchievements.Count)
            {
                SetError("Steam returned an incomplete achievement snapshot. Nothing was posted; Achievement Relay will retry.");
                return;
            }

            var now = DateTimeOffset.UtcNow;
            if (!DateTimeOffset.TryParse(
                    snapshot.ObservedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var observedAt) ||
                observedAt < now - FutureClockTolerance ||
                observedAt > now + FutureClockTolerance)
            {
                SetError("Steam returned an invalid observation timestamp. Nothing was posted; Achievement Relay will retry.");
                return;
            }

            var observations = snapshotAchievements
                .Where(item => !string.IsNullOrWhiteSpace(item.ApiName) && item.ApiName.Length <= 512)
                .Select(item => new SteamAchievementObservation
                {
                    ApiName = item.ApiName.Trim(),
                    Name = string.IsNullOrWhiteSpace(item.Name) ? item.ApiName.Trim() : item.Name.Trim(),
                    Description = string.IsNullOrWhiteSpace(item.Description) ? null : item.Description.Trim(),
                    IsUnlocked = item.IsUnlocked,
                    UnlockedAt = ParseUnlockTime(item.UnlockedAt, observedAt),
                    IconRgba = item.IconRgba,
                    IconWidth = item.IconWidth,
                    IconHeight = item.IconHeight
                })
                .GroupBy(item => item.ApiName, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            if (observations.Length != snapshot.TotalAchievements)
            {
                SetError("Steam returned duplicate or incomplete achievement identities. Nothing was posted; Achievement Relay will retry.");
                return;
            }

            var transitionedApiNames = (snapshot.TransitionedApiNames ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .ToArray();
            var transitionedIds = transitionedApiNames.ToHashSet(StringComparer.Ordinal);
            var unlockedIds = observations
                .Where(item => item.IsUnlocked)
                .Select(item => item.ApiName)
                .ToHashSet(StringComparer.Ordinal);
            if (transitionedIds.Count != transitionedApiNames.Length ||
                transitionedIds.Any(value => !unlockedIds.Contains(value)))
            {
                SetError("Steam returned an invalid achievement transition snapshot. Nothing was posted; Achievement Relay will retry.");
                return;
            }

            processingStage = "state loading";
            var state = await stateStore.LoadAsync(cancellationToken);
            var accounts = state.Accounts.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            accounts.TryGetValue(snapshot.SteamId, out var account);
            var games = (account?.Games ?? new Dictionary<string, SteamGameSyncState>(StringComparer.Ordinal))
                .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
            var appKey = snapshot.AppId.ToString(CultureInfo.InvariantCulture);
            games.TryGetValue(appKey, out var previous);
            var delta = SteamAchievementDeltaDetector.Detect(
                previous?.UnlockedAchievementApiNames,
                observations,
                transitionedIds,
                observedAt);

            var retainedIds = (previous?.UnlockedAchievementApiNames ?? Array.Empty<string>())
                .Concat(delta.CurrentUnlockedApiNames)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            var pendingIds = (previous?.PendingAchievementApiNames ?? Array.Empty<string>())
                .Concat(delta.NewAchievements.Select(item => item.ApiName))
                .Distinct(StringComparer.Ordinal)
                .ToHashSet(StringComparer.Ordinal);

            async Task PersistStateAsync()
            {
                games[appKey] = new SteamGameSyncState
                {
                    MonitoringStartedUtc = detectedAt,
                    LastObservedUtc = observedAt,
                    GameName = game.Name,
                    UnlockedAchievementApiNames = retainedIds,
                    PendingAchievementApiNames = pendingIds
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray()
                };
                accounts[snapshot.SteamId] = new SteamAccountSyncState { Games = games };
                await stateStore.SaveAsync(new SteamSyncState
                {
                    Accounts = accounts
                }, cancellationToken);
            }

            // The transition is durable before any network request. A helper or
            // app restart can therefore retry Discord without reclassifying a
            // historical unlocked state as a new event.
            processingStage = "transition persistence";
            await PersistStateAsync();
            transitionPersisted = pendingIds.Count > 0;

            lock (_statusGate)
            {
                _steamPlayerName = string.IsNullOrWhiteSpace(snapshot.PlayerName) ? null : snapshot.PlayerName.Trim();
                _lastObservationUtc = observedAt;
                _bridgeHasObservation = true;
                _phase = string.IsNullOrWhiteSpace(_lastError)
                    ? SteamMonitoringPhase.Monitoring
                    : SteamMonitoringPhase.Retrying;
            }

            RaiseStatusChanged();

            var pendingAchievements = observations
                // Once a live transition is durable, a later provider relock
                // cannot revoke that delivery proof. Retry it from the current
                // metadata even if Steam temporarily reports the state locked.
                .Where(item => pendingIds.Contains(item.ApiName))
                .OrderBy(item => item.UnlockedAt ?? observedAt)
                .ThenBy(item => item.ApiName, StringComparer.Ordinal)
                .ToArray();
            if (pendingAchievements.Length > 0 && now < _nextDeliveryAttemptUtc)
            {
                return;
            }

            IReadOnlyDictionary<string, double> rarity = new Dictionary<string, double>(StringComparer.Ordinal);
            AppSettings? settings = null;
            if (pendingAchievements.Length > 0)
            {
                processingStage = "optional rarity enrichment";
                rarity = await rarityClient.GetAsync(snapshot.AppId, cancellationToken);
                processingStage = "settings loading";
                settings = await settingsStore.LoadAsync(cancellationToken);
            }

            var posted = 0;
            foreach (var observation in pendingAchievements)
            {
                processingStage = "achievement preparation";
                rarity.TryGetValue(observation.ApiName, out var rarityPercentage);
                var rarityKnown = rarity.ContainsKey(observation.ApiName);
                var rarityTier = RelayRarityClassifier.Classify(
                    rarityKnown ? rarityPercentage : null);
                byte[]? icon = null;
                if (observation.IconRgba is { Length: > 0 })
                {
                    try
                    {
                        icon = RgbaPngEncoder.Encode(
                            observation.IconWidth,
                            observation.IconHeight,
                            observation.IconRgba);
                    }
                    catch (Exception exception) when (exception is ArgumentException or OverflowException)
                    {
                        icon = null;
                    }
                }

                var reportedTimeIsUsable = observation.UnlockedAt is { } unlockedAt &&
                                           unlockedAt <= observedAt + FutureClockTolerance;
                var achievement = new AchievementEvent
                {
                    Id = SteamAchievementDeltaDetector.CreateEventId(snapshot.SteamId, snapshot.AppId, observation.ApiName),
                    Name = observation.Name,
                    Description = observation.Description,
                    GameName = game.Name,
                    IsRare = rarityTier is RelayRarityTier.Gold or RelayRarityTier.Platinum,
                    RarityKnown = rarityKnown,
                    RarityPercentage = rarityKnown ? rarityPercentage : null,
                    HeroImageUrl = $"https://cdn.akamai.steamstatic.com/steam/apps/{snapshot.AppId.ToString(CultureInfo.InvariantCulture)}/library_hero.jpg",
                    ImageBytes = icon,
                    ImageFileName = icon is null ? null : "steam-achievement.png",
                    ImageContentType = icon is null ? null : "image/png",
                    PlayerName = string.IsNullOrWhiteSpace(snapshot.PlayerName) ? null : snapshot.PlayerName.Trim(),
                    SourceProvider = "Steam",
                    Platform = "Steam",
                    UnlockedAt = reportedTimeIsUsable ? observation.UnlockedAt : observedAt,
                    UnlockTimeEstimated = !reportedTimeIsUsable
                };
                processingStage = "Discord delivery";
                var delivery = await deliveryService.DeliverAsync(achievement, settings!, cancellationToken);
                if (delivery == AchievementDeliveryResult.RetryRequired)
                {
                    _consecutiveDeliveryFailures = Math.Min(_consecutiveDeliveryFailures + 1, 5);
                    var retryDelay = GetDeliveryBackoff(_consecutiveDeliveryFailures);
                    _nextDeliveryAttemptUtc = DateTimeOffset.UtcNow + retryDelay;
                    SetError($"Discord delivery is pending for {achievement.Name}; the live transition is stored safely and will retry in about {retryDelay.TotalMinutes:0} minute{(retryDelay == TimeSpan.FromMinutes(1) ? string.Empty : "s")}.");
                    return;
                }

                _consecutiveDeliveryFailures = 0;
                _nextDeliveryAttemptUtc = DateTimeOffset.MinValue;
                pendingIds.Remove(observation.ApiName);
                processingStage = "delivery-state persistence";
                await PersistStateAsync();
                if (delivery == AchievementDeliveryResult.Posted)
                {
                    posted++;
                }
            }

            lock (_statusGate)
            {
                _steamPlayerName = string.IsNullOrWhiteSpace(snapshot.PlayerName) ? null : snapshot.PlayerName.Trim();
                _lastObservationUtc = observedAt;
                _lastError = null;
                _bridgeHasObservation = true;
                _phase = SteamMonitoringPhase.Monitoring;
            }

            if (snapshot.InitialSnapshot)
            {
                activityLog.Success($"Steam baseline established for {game.Name}. Existing unlocks were not sent to Discord.");
            }
            else if (posted > 0)
            {
                activityLog.Success($"Posted {posted} new Steam achievement{(posted == 1 ? string.Empty : "s")} for {game.Name}.");
            }

            RaiseStatusChanged();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var recovery = transitionPersisted
                ? "The live transition is stored safely and will retry."
                : "Nothing unverified was posted; Achievement Relay will retry.";
            SetError($"Steam observation processing failed safely during {processingStage} ({exception.GetType().Name}). {recovery}");
        }
        finally
        {
            _snapshotGate.Release();
        }
    }

    private bool IsBridgeAlive()
    {
        var process = _bridgeProcess;
        try
        {
            return process is not null && !process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void CheckBridgeStartupTimeout(SteamGameInfo? game, DateTimeOffset now)
    {
        if (game is null || !IsBridgeAlive())
        {
            return;
        }

        DateTimeOffset? startedAt;
        bool hasObservation;
        lock (_statusGate)
        {
            startedAt = _bridgeStartedUtc;
            hasObservation = _bridgeHasObservation;
        }

        if (hasObservation || startedAt is null || now - startedAt.Value < InitialObservationTimeout)
        {
            return;
        }

        SetError($"Steam did not provide a complete achievement baseline for {game.Name} within 45 seconds. Achievement Relay will restart the observer automatically.");
        var process = _bridgeProcess;
        if (process is not null)
        {
            TryTerminateBridge(process);
        }
    }

    private void SetPhase(SteamMonitoringPhase phase)
    {
        var changed = false;
        lock (_statusGate)
        {
            changed = _phase != phase;
            _phase = phase;
        }

        if (changed)
        {
            RaiseStatusChanged();
        }
    }

    private void SetError(string message)
    {
        var changed = false;
        lock (_statusGate)
        {
            changed = !string.Equals(_lastError, message, StringComparison.Ordinal);
            _lastError = message;
            _phase = SteamMonitoringPhase.Retrying;
        }

        if (changed)
        {
            activityLog.Warning(message);
        }

        RaiseStatusChanged();
    }

    private void RaiseStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    private static DateTimeOffset? ParseUnlockTime(string? value, DateTimeOffset observedAt)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed) ||
            parsed.Year < 2000 ||
            parsed > observedAt + FutureClockTolerance)
        {
            return null;
        }

        return parsed;
    }

    private static TimeSpan GetDeliveryBackoff(int consecutiveFailures) => consecutiveFailures switch
    {
        <= 1 => TimeSpan.FromMinutes(1),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(5),
        4 => TimeSpan.FromMinutes(15),
        _ => TimeSpan.FromMinutes(30)
    };

    private static string GetBridgePath() => Path.Combine(
        AppContext.BaseDirectory,
        "SteamBridge",
        "AchievementRelay.SteamBridge.exe");

    private sealed record SteamBridgeMessage
    {
        [JsonPropertyName("protocolVersion")]
        public int ProtocolVersion { get; init; }

        [JsonPropertyName("type")]
        public string Type { get; init; } = string.Empty;

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("message")]
        public string? Message { get; init; }

        [JsonPropertyName("appId")]
        public uint AppId { get; init; }

        [JsonPropertyName("steamId")]
        public string SteamId { get; init; } = string.Empty;

        [JsonPropertyName("playerName")]
        public string? PlayerName { get; init; }

        [JsonPropertyName("observedAt")]
        public string? ObservedAt { get; init; }

        [JsonPropertyName("totalAchievements")]
        public int TotalAchievements { get; init; }

        [JsonPropertyName("complete")]
        public bool Complete { get; init; }

        [JsonPropertyName("initialSnapshot")]
        public bool InitialSnapshot { get; init; }

        [JsonPropertyName("transitionedApiNames")]
        public IReadOnlyList<string>? TransitionedApiNames { get; init; }

        [JsonPropertyName("achievements")]
        public IReadOnlyList<SteamBridgeAchievement>? Achievements { get; init; }
    }

    private sealed record SteamBridgeAchievement
    {
        [JsonPropertyName("apiName")]
        public string ApiName { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("isUnlocked")]
        public bool IsUnlocked { get; init; }

        [JsonPropertyName("unlockedAt")]
        public string? UnlockedAt { get; init; }

        [JsonPropertyName("iconRgba")]
        public byte[]? IconRgba { get; init; }

        [JsonPropertyName("iconWidth")]
        public int IconWidth { get; init; }

        [JsonPropertyName("iconHeight")]
        public int IconHeight { get; init; }
    }
}
