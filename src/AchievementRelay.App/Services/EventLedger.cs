using System.IO;
using System.Text.Json;

namespace AchievementRelay.App.Services;

public sealed class EventLedger(AppPaths paths)
{
    private const int MaximumEntries = 1_000;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromDays(90);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<string, DateTimeOffset>? _entries;

    public async Task<bool> ContainsAsync(string eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            return _entries!.ContainsKey(eventId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkProcessedAsync(string eventId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureLoadedAsync(cancellationToken);
            _entries![eventId] = DateTimeOffset.UtcNow;
            Prune();
            await SaveAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_entries is not null)
        {
            return;
        }

        if (!File.Exists(paths.EventLedgerFile))
        {
            _entries = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            return;
        }

        try
        {
            await using var stream = File.OpenRead(paths.EventLedgerFile);
            _entries = await JsonSerializer.DeserializeAsync<Dictionary<string, DateTimeOffset>>(
                stream,
                JsonOptions,
                cancellationToken) ?? new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            _entries = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        }
        catch (IOException)
        {
            _entries = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        }
    }

    private void Prune()
    {
        var entries = _entries ?? throw new InvalidOperationException("The event ledger has not been loaded.");
        var cutoff = DateTimeOffset.UtcNow - MaximumAge;
        foreach (var oldEntry in entries.Where(entry => entry.Value < cutoff).Select(entry => entry.Key).ToArray())
        {
            entries.Remove(oldEntry);
        }

        if (entries.Count <= MaximumEntries)
        {
            return;
        }

        foreach (var overflow in entries
            .OrderBy(entry => entry.Value)
            .Take(entries.Count - MaximumEntries)
            .Select(entry => entry.Key)
            .ToArray())
        {
            entries.Remove(overflow);
        }
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        var temporaryFile = string.Concat(paths.EventLedgerFile, ".tmp");
        await using (var stream = new FileStream(
            temporaryFile,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, _entries, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryFile, paths.EventLedgerFile, true);
    }
}
