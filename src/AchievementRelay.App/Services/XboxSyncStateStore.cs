using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed record XboxSyncState
{
    public const int CurrentSchemaVersion = 7;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string AccountXuid { get; init; } = string.Empty;

    public DateTimeOffset? BaselineUtc { get; init; }

    public DateTimeOffset? LastSuccessfulPollUtc { get; init; }

    public DateTimeOffset? LastBackgroundWorkUtc { get; init; }

    public Dictionary<string, XboxTitleSnapshot> Titles { get; init; } = new(StringComparer.Ordinal);

    public Dictionary<string, XboxTitleSyncWork> PendingTitles { get; init; } = new(StringComparer.Ordinal);
}

public sealed record XboxTitleSnapshot
{
    public int CurrentAchievements { get; init; }

    public int CurrentGamerscore { get; init; }

    /// <summary>
    /// Bounded title-history device hints. Multiple or unfamiliar families
    /// intentionally remain ambiguous when a Discord platform is selected.
    /// </summary>
    public string[] Devices { get; init; } = [];

    public string? DisplayImageUrl { get; init; }

    /// <summary>
    /// Null means the title has only an unverified count snapshot, whether it
    /// came from an older schema or a newly observed title. An empty array is
    /// a detail-verified baseline for a title with no unlocked achievements.
    /// </summary>
    public string[]? UnlockedAchievementIds { get; init; }

    [JsonIgnore]
    public bool HasAchievementIdentityBaseline => UnlockedAchievementIds is not null;
}

public sealed class XboxSyncStateStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<XboxSyncState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(paths.XboxSyncStateFile))
            {
                return new XboxSyncState();
            }

            await using var stream = File.OpenRead(paths.XboxSyncStateFile);
            var state = await JsonSerializer.DeserializeAsync<XboxSyncState>(stream, JsonOptions, cancellationToken);
            if (state is null || state.SchemaVersion is < 2 or > XboxSyncState.CurrentSchemaVersion)
            {
                return new XboxSyncState();
            }

            var sourceSchemaVersion = state.SchemaVersion;

            return state with
            {
                SchemaVersion = XboxSyncState.CurrentSchemaVersion,
                Titles = new Dictionary<string, XboxTitleSnapshot>(
                    (state.Titles ?? new Dictionary<string, XboxTitleSnapshot>())
                        .ToDictionary(
                            entry => entry.Key,
                            entry => NormalizeSnapshot(entry.Value, sourceSchemaVersion),
                            StringComparer.Ordinal),
                    StringComparer.Ordinal),
                PendingTitles = new Dictionary<string, XboxTitleSyncWork>(
                    (state.PendingTitles ?? new Dictionary<string, XboxTitleSyncWork>())
                        .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                        .ToDictionary(
                            entry => entry.Key,
                            entry => NormalizePendingWork(entry.Key, entry.Value, sourceSchemaVersion),
                            StringComparer.Ordinal),
                    StringComparer.Ordinal)
            };
        }
        catch (JsonException)
        {
            return new XboxSyncState();
        }
        catch (IOException)
        {
            return new XboxSyncState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task ResetAsync(
        string accountXuid,
        DateTimeOffset baselineUtc,
        IEnumerable<XboxTitleProgress> titles,
        CancellationToken cancellationToken = default) =>
        SaveAsync(new XboxSyncState
        {
            AccountXuid = accountXuid,
            BaselineUtc = baselineUtc.ToUniversalTime(),
            LastSuccessfulPollUtc = baselineUtc.ToUniversalTime(),
            Titles = CreateTitleSnapshots(titles)
        }, cancellationToken);

    public static Dictionary<string, XboxTitleSnapshot> CreateTitleSnapshots(
        IEnumerable<XboxTitleProgress> titles,
        IReadOnlyDictionary<string, XboxTitleSnapshot>? previousTitles = null,
        bool retainMissingTitles = false)
    {
        ArgumentNullException.ThrowIfNull(titles);

        var snapshots = retainMissingTitles && previousTitles is not null
            ? previousTitles.ToDictionary(
                entry => entry.Key,
                entry => NormalizeSnapshot(entry.Value),
                StringComparer.Ordinal)
            : new Dictionary<string, XboxTitleSnapshot>(StringComparer.Ordinal);

        foreach (var group in titles.GroupBy(title => title.TitleId, StringComparer.Ordinal))
        {
            var title = group
                .OrderByDescending(item => item.CurrentAchievements)
                .ThenByDescending(item => item.CurrentGamerscore)
                .ThenByDescending(item => item.LastPlayedAt)
                .First();
            XboxTitleSnapshot? previous = null;
            if (previousTitles is not null)
            {
                previousTitles.TryGetValue(group.Key, out previous);
            }

            snapshots[group.Key] = new XboxTitleSnapshot
            {
                // Xbox achievement totals should not regress. Retaining the
                // larger durable values prevents a partial provider page from
                // shrinking state and making old identities look new later.
                CurrentAchievements = Math.Max(
                    title.CurrentAchievements,
                    previous?.CurrentAchievements ?? 0),
                CurrentGamerscore = Math.Max(
                    title.CurrentGamerscore,
                    previous?.CurrentGamerscore ?? 0),
                Devices = XboxPlatformClassifier.NormalizeDevices(
                    group.SelectMany(item => item.Devices),
                    previous?.Devices),
                DisplayImageUrl = FirstUrlHint(
                    group.Select(item => item.DisplayImageUrl)
                        .Append(previous?.DisplayImageUrl)),
                // Counts alone never prove an identity baseline, including a
                // reported zero. Leave new titles unverified until a complete
                // detail response has been silently hydrated.
                UnlockedAchievementIds = previous?.UnlockedAchievementIds
            };
        }

        return snapshots;
    }

    public async Task SaveAsync(XboxSyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryFile = string.Concat(paths.XboxSyncStateFile, ".tmp");
            await using (var stream = new FileStream(
                temporaryFile,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    state with { SchemaVersion = XboxSyncState.CurrentSchemaVersion },
                    JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryFile, paths.XboxSyncStateFile, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(paths.XboxSyncStateFile))
            {
                File.Delete(paths.XboxSyncStateFile);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static XboxTitleSnapshot NormalizeSnapshot(
        XboxTitleSnapshot? snapshot,
        int sourceSchemaVersion = XboxSyncState.CurrentSchemaVersion)
    {
        snapshot ??= new XboxTitleSnapshot();
        var normalizedIds = snapshot.UnlockedAchievementIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        // Schema 3 treated a summary count of zero as a verified empty ID
        // baseline without fetching details. Re-open those snapshots once so
        // a later provider correction cannot turn old history into new posts.
        if (sourceSchemaVersion < 4 &&
            snapshot.CurrentAchievements == 0 &&
            normalizedIds is { Length: 0 })
        {
            normalizedIds = null;
        }

        return snapshot with
        {
            UnlockedAchievementIds = normalizedIds,
            Devices = XboxPlatformClassifier.NormalizeDevices(snapshot.Devices),
            DisplayImageUrl = NormalizeUrlHint(snapshot.DisplayImageUrl)
        };
    }

    private static XboxTitleSyncWork NormalizePendingWork(
        string titleId,
        XboxTitleSyncWork? work,
        int sourceSchemaVersion)
    {
        work ??= new XboxTitleSyncWork();
        var firstObservedUtc = work.FirstObservedUtc.ToUniversalTime();
        var lastObservedUtc = work.LastObservedUtc.ToUniversalTime();
        var liveDeliveryEpochUtc = sourceSchemaVersion >= 6
            ? work.LiveDeliveryEpochUtc?.ToUniversalTime()
            : null;
        var hasValidLiveEvidence = liveDeliveryEpochUtc is { } liveEpoch &&
                                   liveEpoch != default &&
                                   firstObservedUtc != default &&
                                   lastObservedUtc != default &&
                                   liveEpoch <= firstObservedUtc &&
                                   firstObservedUtc <= lastObservedUtc;

        return work with
        {
            TitleId = titleId,
            Name = string.IsNullOrWhiteSpace(work.Name) ? null : work.Name.Trim(),
            CurrentAchievements = Math.Max(0, work.CurrentAchievements),
            CurrentGamerscore = Math.Max(0, work.CurrentGamerscore),
            LastPlayedAt = work.LastPlayedAt?.ToUniversalTime(),
            Devices = XboxPlatformClassifier.NormalizeDevices(work.Devices),
            DisplayImageUrl = NormalizeUrlHint(work.DisplayImageUrl),
            FirstObservedUtc = firstObservedUtc,
            LastObservedUtc = lastObservedUtc,
            // Older schemas did not record whether queued work had direct
            // live-session evidence. Fail closed during migration instead of
            // risking a cross-device historical repost.
            LiveDeliveryEpochUtc = hasValidLiveEvidence ? liveDeliveryEpochUtc : null,
            AllowsUntimestampedDelivery = hasValidLiveEvidence &&
                                          work.AllowsUntimestampedDelivery
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

    private static string? FirstUrlHint(IEnumerable<string?> values)
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
