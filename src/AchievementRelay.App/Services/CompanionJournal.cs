using System.IO;
using System.Text.Json;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed record JournalEntry(AchievementEvent Achievement, DateTimeOffset ObservedAt,
    string SessionId, string Delivery, DateTimeOffset UpdatedAt);

/// <summary>Local presentation history. Never authorizes historical delivery.</summary>
public sealed class CompanionJournal
{
    private readonly string _path;
    private readonly ActivityLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private JournalEntry[] _entries = [];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public event Action? Changed;
    public string? StorageError { get; private set; }
    public JournalEntry[] Snapshot => Volatile.Read(ref _entries).ToArray();

    public CompanionJournal(AppPaths paths, ActivityLog log)
    {
        _path = Path.Combine(paths.DataDirectory, "companion-journal.json");
        _log = log;
        try
        {
            if (File.Exists(_path))
            {
                if (new FileInfo(_path).Length > 24_000_000) throw new InvalidDataException();
                _entries = (JsonSerializer.Deserialize<JournalEntry[]>(File.ReadAllText(_path), Json) ?? [])
                    .Where(x => x?.Achievement is not null && !string.IsNullOrWhiteSpace(x.Achievement.Id))
                    .TakeLast(300).ToArray();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { StorageError = "History could not be loaded. Existing history has been preserved."; }
    }

    public async Task RecordAsync(AchievementEvent achievement, string delivery, byte[]? icon = null)
    {
        await _gate.WaitAsync();
        try
        {
            if (StorageError is not null) return;
            var now = DateTimeOffset.UtcNow;
            var previous = _entries.FirstOrDefault(x => x.Achievement.Id == achievement.Id);
            var latest = _entries.MaxBy(x => x.ObservedAt);
            var session = previous?.SessionId ?? (latest is not null && now - latest.ObservedAt < TimeSpan.FromMinutes(30)
                ? latest.SessionId : Guid.NewGuid().ToString("N"));
            var bytes = icon ?? achievement.ImageBytes ?? previous?.Achievement.ImageBytes;
            static string? Bound(string? value, int count) => value is null ? null : value[..Math.Min(value.Length, count)];
            var safe = achievement with { ImageBytes = bytes is { Length: <= 32_000 } ? bytes.ToArray() : null,
                Name = Bound(achievement.Name, 256)!, Description = Bound(achievement.Description, 3000),
                GameName = Bound(achievement.GameName, 256), PlayerName = Bound(achievement.PlayerName, 128),
                ImageUrl = Bound(achievement.ImageUrl, 2048), HeroImageUrl = Bound(achievement.HeroImageUrl, 2048) };
            var entry = new JournalEntry(safe, previous?.ObservedAt ?? now, session, delivery, now);
            var next = _entries.Where(x => x.Achievement.Id != achievement.Id).Append(entry)
                .OrderBy(x => x.ObservedAt).TakeLast(300).ToArray();
            var temp = _path + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(next, Json));
            File.Move(temp, _path, true);
            Volatile.Write(ref _entries, next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StorageError = "History could not be saved. Check available disk space and folder access."; _log.Warning(StorageError); }
        finally { _gate.Release(); }
        foreach (var callback in Changed?.GetInvocationList() ?? [])
        { try { ((Action)callback)(); } catch (Exception) { } }
    }
}
