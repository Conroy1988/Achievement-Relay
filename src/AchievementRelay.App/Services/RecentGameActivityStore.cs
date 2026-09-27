using System.IO;
using System.Text.Json;

namespace AchievementRelay.App.Services;

public sealed record RecentGameActivity(string GameId, string Name, string Provider, DateTimeOffset PlayedAt);

/// <summary>Local play evidence, independent of progress polling and achievement delivery.</summary>
public sealed class RecentGameActivityStore
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private RecentGameActivity? _latest;
    public RecentGameActivity? Latest => Volatile.Read(ref _latest);
    public string? Error { get; private set; }

    public RecentGameActivityStore(AppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "recent-game-activity.json");
        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 16_384) throw new InvalidDataException();
                _latest = JsonSerializer.Deserialize<RecentGameActivity>(File.ReadAllText(_path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Error = "Recent game activity could not be loaded; existing data has been preserved."; }
    }

    public async Task RecordAsync(string gameId, string name, string provider, DateTimeOffset? playedAt)
    {
        // A background sync time is never evidence of play.
        if (playedAt is null || playedAt > DateTimeOffset.UtcNow || playedAt <= DateTimeOffset.UnixEpoch) return;
        await _gate.WaitAsync();
        try
        {
            if (Error is not null) return;
            var previous = Latest;
            if (previous is not null && (playedAt <= previous.PlayedAt ||
                (previous.GameId == gameId && previous.Provider == provider && playedAt - previous.PlayedAt < TimeSpan.FromMinutes(1)))) return;
            var next = new RecentGameActivity(gameId, name, provider, playedAt.Value);
            await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(next));
            File.Move(_path + ".tmp", _path, true);
            Volatile.Write(ref _latest, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Error = "Recent game activity could not be saved. Live monitoring is unaffected."; }
        finally { _gate.Release(); }
    }
}
