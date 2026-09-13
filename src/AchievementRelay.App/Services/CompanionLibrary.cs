using System.IO;
using System.Text.Json;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed record LibraryGame(string Key, string Name, string Provider, int Earned, int? Total,
    string? Artwork, DateTimeOffset ObservedAt, AchievementEvent[] History);

/// <summary>Read-only presentation snapshots. This store has no delivery dependency.</summary>
public sealed class CompanionLibrary
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LibraryGame[] _games = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public LibraryGame[] Snapshot => Volatile.Read(ref _games).ToArray();
    public string? Error { get; private set; }
    public CompanionLibrary(AppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "companion-library.json");
        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 20_000_000) throw new InvalidDataException();
                _games = (JsonSerializer.Deserialize<LibraryGame[]>(File.ReadAllText(_path), Json) ?? [])
                    .Where(x => x is not null && x.History is not null).TakeLast(100).ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { Error = "Library could not be loaded; existing data has been preserved."; }
    }
    public async Task ObserveAsync(string key, string name, string provider, int earned, int? total,
        string? artwork, IEnumerable<AchievementEvent> history, bool import)
    {
        await _gate.WaitAsync();
        try
        {
            if (Error is not null) return;
            var previous = _games.FirstOrDefault(x => x.Key == key);
            // Historical data is opt-in, bounded, and never placed in the live journal.
            var entries = import ? history.Take(300).Select(x => x with
            {
                ImageBytes = null, PlayerName = null, IsGameCompletion = false, IsHistorical = true,
                Name = x.Name[..Math.Min(x.Name.Length, 256)],
                Description = x.Description is { } description ? description[..Math.Min(description.Length, 2000)] : null
            }).ToArray() : previous?.History ?? [];
            if (previous is not null && previous.Earned == earned && previous.Total == total && previous.History.Length == entries.Length &&
                DateTimeOffset.UtcNow - previous.ObservedAt < TimeSpan.FromMinutes(2)) return;
            var game = new LibraryGame(key, name, provider, Math.Max(0, earned), total is > 0 && total >= earned ? total : null,
                artwork, DateTimeOffset.UtcNow, entries);
            var next = _games.Where(x => x.Key != key).Append(game).TakeLast(100).ToArray();
            // Cap total imported metadata independently of provider size.
            var remaining = 3000;
            for (var i = next.Length - 1; i >= 0; i--)
            { next[i] = next[i] with { History = next[i].History.Take(remaining).ToArray() }; remaining -= next[i].History.Length; }
            await File.WriteAllTextAsync(_path + ".tmp", JsonSerializer.Serialize(next, Json));
            File.Move(_path + ".tmp", _path, true); Volatile.Write(ref _games, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Error = "Library could not be saved. Live monitoring is unaffected."; }
        finally { _gate.Release(); }
    }
}
