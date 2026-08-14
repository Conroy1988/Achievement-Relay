using System.Text;
using System.Text.RegularExpressions;

namespace AchievementRelay.App.Services;

public enum ActivityLevel
{
    Information,
    Success,
    Warning,
    Error
}

public sealed record ActivityEntry(DateTimeOffset Timestamp, ActivityLevel Level, string Message);

public sealed class ActivityLog(AppPaths paths)
{
    private static readonly Regex DiscordWebhookPathPattern = new(
        @"/api(?:/v\d+)?/webhooks/[^\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SemaphoreSlim _fileGate = new(1, 1);

    public event EventHandler<ActivityEntry>? EntryAdded;

    public void Info(string message) => Write(ActivityLevel.Information, message);

    public void Success(string message) => Write(ActivityLevel.Success, message);

    public void Warning(string message) => Write(ActivityLevel.Warning, message);

    public void Error(string message) => Write(ActivityLevel.Error, message);

    private void Write(ActivityLevel level, string message)
    {
        var safeMessage = Sanitize(message);
        var entry = new ActivityEntry(DateTimeOffset.Now, level, safeMessage);
        EntryAdded?.Invoke(this, entry);
        _ = AppendToFileAsync(entry);
    }

    private async Task AppendToFileAsync(ActivityEntry entry)
    {
        try
        {
            await _fileGate.WaitAsync();
            var line = $"{entry.Timestamp:O} [{entry.Level}] {entry.Message}{Environment.NewLine}";
            await File.AppendAllTextAsync(paths.LogFile, line, Encoding.UTF8);
            TrimLogIfNeeded();
        }
        catch (IOException)
        {
            // Logging must never interrupt achievement delivery.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging must never interrupt achievement delivery.
        }
        finally
        {
            if (_fileGate.CurrentCount == 0)
            {
                _fileGate.Release();
            }
        }
    }

    private void TrimLogIfNeeded()
    {
        var file = new FileInfo(paths.LogFile);
        if (!file.Exists || file.Length < 2_000_000)
        {
            return;
        }

        var lines = File.ReadLines(paths.LogFile).TakeLast(2_000).ToArray();
        File.WriteAllLines(paths.LogFile, lines, Encoding.UTF8);
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "No additional information.";
        }

        return DiscordWebhookPathPattern.Replace(value.Trim(), "/api/webhooks/[redacted]");
    }
}
