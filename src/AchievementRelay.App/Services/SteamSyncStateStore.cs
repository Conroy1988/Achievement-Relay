using System.IO;
using System.Text.Json;

namespace AchievementRelay.App.Services;

public sealed record SteamGameSyncState
{
    public DateTimeOffset MonitoringStartedUtc { get; init; }

    public DateTimeOffset LastObservedUtc { get; init; }

    public string GameName { get; init; } = string.Empty;

    public IReadOnlyCollection<string> UnlockedAchievementApiNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Live transitions that were durably observed but have not yet been
    /// accepted (or intentionally filtered) by the shared delivery service.
    /// Persisting these identities before the webhook call lets Discord retry
    /// safely across helper and app restarts without treating history as new.
    /// </summary>
    public IReadOnlyCollection<string> PendingAchievementApiNames { get; init; } = Array.Empty<string>();
}

public sealed record SteamAccountSyncState
{
    public IReadOnlyDictionary<string, SteamGameSyncState> Games { get; init; } =
        new Dictionary<string, SteamGameSyncState>(StringComparer.Ordinal);
}

public sealed record SteamSyncState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyDictionary<string, SteamAccountSyncState> Accounts { get; init; } =
        new Dictionary<string, SteamAccountSyncState>(StringComparer.Ordinal);
}

public sealed class SteamSyncStateStore(AppPaths paths)
{
    private const int MaximumGames = 250;
    private const int MaximumAccounts = 8;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<SteamSyncState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(paths.SteamSyncStateFile))
            {
                return new SteamSyncState();
            }

            await using var stream = File.OpenRead(paths.SteamSyncStateFile);
            var state = await JsonSerializer.DeserializeAsync<SteamSyncState>(stream, JsonOptions, cancellationToken);
            return state?.SchemaVersion == SteamSyncState.CurrentSchemaVersion
                ? Normalize(state)
                : new SteamSyncState();
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return new SteamSyncState();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(SteamSyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var normalized = Normalize(state);
            var temporaryFile = string.Concat(paths.SteamSyncStateFile, ".tmp");
            await using (var stream = new FileStream(
                             temporaryFile,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, normalized, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryFile, paths.SteamSyncStateFile, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static SteamSyncState Normalize(SteamSyncState state)
    {
        var accounts = (state.Accounts ?? new Dictionary<string, SteamAccountSyncState>(StringComparer.Ordinal))
            .Where(item => ulong.TryParse(item.Key, out var steamId) && steamId > 0 && item.Value is not null)
            .OrderByDescending(item => (item.Value.Games ?? new Dictionary<string, SteamGameSyncState>())
                .Values
                .Any(value => value?.PendingAchievementApiNames is { Count: > 0 }))
            .ThenByDescending(item => (item.Value.Games ?? new Dictionary<string, SteamGameSyncState>())
                .Values
                .Where(value => value is not null)
                .Select(value => value.LastObservedUtc)
                .DefaultIfEmpty(DateTimeOffset.MinValue)
                .Max())
            .Take(MaximumAccounts)
            .ToDictionary(
                item => item.Key,
                item => new SteamAccountSyncState
                {
                    Games = NormalizeGames(item.Value.Games)
                },
                StringComparer.Ordinal);
        return state with
        {
            SchemaVersion = SteamSyncState.CurrentSchemaVersion,
            Accounts = accounts
        };
    }

    private static IReadOnlyDictionary<string, SteamGameSyncState> NormalizeGames(
        IReadOnlyDictionary<string, SteamGameSyncState>? source) =>
        (source ?? new Dictionary<string, SteamGameSyncState>(StringComparer.Ordinal))
            .Where(item => uint.TryParse(item.Key, out var appId) && appId > 0 && item.Value is not null)
            .OrderByDescending(item => item.Value.PendingAchievementApiNames is { Count: > 0 })
            .ThenByDescending(item => item.Value.LastObservedUtc)
            .Take(MaximumGames)
            .ToDictionary(
                item => item.Key,
                item => item.Value with
                {
                    GameName = string.IsNullOrWhiteSpace(item.Value.GameName)
                        ? string.Empty
                        : item.Value.GameName.Length <= 256
                            ? item.Value.GameName
                            : item.Value.GameName[..256],
                    UnlockedAchievementApiNames = (item.Value.UnlockedAchievementApiNames ?? Array.Empty<string>())
                        .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 512)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray(),
                    PendingAchievementApiNames = (item.Value.PendingAchievementApiNames ?? Array.Empty<string>())
                        .Where(value => !string.IsNullOrWhiteSpace(value) && value.Length <= 512)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(value => value, StringComparer.Ordinal)
                        .ToArray()
                },
                StringComparer.Ordinal);
}
