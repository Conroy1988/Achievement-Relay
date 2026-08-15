using System.IO;
using System.Text.Json;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed record XboxSyncState
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string AccountXuid { get; init; } = string.Empty;

    public DateTimeOffset? BaselineUtc { get; init; }

    public DateTimeOffset? LastSuccessfulPollUtc { get; init; }

    public Dictionary<string, XboxTitleSnapshot> Titles { get; init; } = new(StringComparer.Ordinal);
}

public sealed record XboxTitleSnapshot(int CurrentAchievements, int CurrentGamerscore);

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
            if (state is null || state.SchemaVersion != XboxSyncState.CurrentSchemaVersion)
            {
                return new XboxSyncState();
            }

            return state with
            {
                Titles = new Dictionary<string, XboxTitleSnapshot>(
                    state.Titles ?? new Dictionary<string, XboxTitleSnapshot>(),
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
        IEnumerable<XboxTitleProgress> titles)
    {
        ArgumentNullException.ThrowIfNull(titles);

        return titles
            .GroupBy(title => title.TitleId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var title = group.First();
                    return new XboxTitleSnapshot(title.CurrentAchievements, title.CurrentGamerscore);
                },
                StringComparer.Ordinal);
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
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
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
}
