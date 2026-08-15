using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed record XboxSyncState
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string AccountXuid { get; init; } = string.Empty;

    public DateTimeOffset? BaselineUtc { get; init; }

    public DateTimeOffset? LastSuccessfulPollUtc { get; init; }

    public Dictionary<string, XboxTitleSnapshot> Titles { get; init; } = new(StringComparer.Ordinal);
}

public sealed record XboxTitleSnapshot
{
    public int CurrentAchievements { get; init; }

    public int CurrentGamerscore { get; init; }

    /// <summary>
    /// Null means this title came from a schema-v2/count-only baseline. An
    /// empty array is a complete baseline for a title with no unlocked
    /// achievements.
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

            return state with
            {
                SchemaVersion = XboxSyncState.CurrentSchemaVersion,
                Titles = new Dictionary<string, XboxTitleSnapshot>(
                    (state.Titles ?? new Dictionary<string, XboxTitleSnapshot>())
                        .ToDictionary(
                            entry => entry.Key,
                            entry => NormalizeSnapshot(entry.Value),
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
            var title = group.First();
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
                UnlockedAchievementIds = previous?.UnlockedAchievementIds ??
                                         (title.CurrentAchievements == 0 ? [] : null)
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

    private static XboxTitleSnapshot NormalizeSnapshot(XboxTitleSnapshot? snapshot)
    {
        snapshot ??= new XboxTitleSnapshot();
        return snapshot with
        {
            UnlockedAchievementIds = snapshot.UnlockedAchievementIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray()
        };
    }
}
