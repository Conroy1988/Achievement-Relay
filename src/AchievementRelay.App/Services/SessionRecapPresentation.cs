using System.Globalization;
using System.Text;
using System.Text.Json;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

/// <summary>Immutable preview snapshot. No delivery, persistence or automatic sharing.</summary>
public sealed record SessionRecapPresentation(int Unlocks, int Games, long Gamerscore, string Content)
{
    public string JsonPayload => JsonSerializer.Serialize(new { username = "Achievement Relay", content = Content,
        allowed_mentions = new { parse = Array.Empty<string>() } });

    public static SessionRecapPresentation Create(IEnumerable<JournalEntry> source, TimeZoneInfo zone)
    {
        var entries = source.Where(x => !x.Achievement.IsHistorical && !x.Achievement.IsGameCompletion)
            .GroupBy(x => x.Achievement.Id).Select(x => x.MaxBy(e => e.UpdatedAt)!)
            .OrderBy(x => x.ObservedAt).ToArray();
        if (entries.Length == 0) return new(0, 0, 0, "No live unlocks recorded in this session.");
        var groups = entries.GroupBy(x => (x.Achievement.SourceProvider, x.Achievement.GameName))
            .OrderByDescending(x => x.Count()).ThenBy(x => x.Key.GameName, StringComparer.OrdinalIgnoreCase).ToArray();
        var score = entries.Sum(x => (long)Math.Max(0, x.Achievement.Gamerscore ?? 0));
        var rarest = entries.Where(x => x.Achievement.RarityKnown && x.Achievement.RarityPercentage is >= 0 and <= 100)
            .MinBy(x => x.Achievement.RarityPercentage);
        var start = TimeZoneInfo.ConvertTime(entries[0].ObservedAt, zone);
        var end = TimeZoneInfo.ConvertTime(entries[^1].ObservedAt, zone);
        var content = new StringBuilder("TKB REDLINE · SESSION RECAP\n")
            .AppendLine($"{entries.Length} unlocks · {groups.Length} game/platform groups · {score}G")
            .AppendLine($"Observed {start:yyyy-MM-dd HH:mm} UTC{start:zzz} – {end:yyyy-MM-dd HH:mm} UTC{end:zzz}")
            .AppendLine();
        var shown = 0;
        foreach (var group in groups.Take(8)) {
            var line = $"{Safe(group.Key.GameName ?? "Unknown game", 64)} · {Safe(group.Key.SourceProvider, 20)} — {group.Count()} unlocks";
            if (content.Length + line.Length > 1450) break;
            content.AppendLine(line); shown++;
        }
        if (groups.Length > shown) content.AppendLine($"+ {groups.Length - shown} more game/platform groups");
        content.AppendLine().AppendLine(rarest is null ? "Rarest: global rarity unavailable."
            : $"Rarest: {Safe(rarest.Achievement.Name, 64)} · {RelayRarityClassifier.FormatPercentage(rarest.Achievement.RarityPercentage)} of players");
        content.Append("Recorded on this PC; not a full account history. Imported history and completion celebration events are excluded.");
        return new(entries.Length, groups.Length, score, content.ToString());
    }

    private static string Safe(string text, int maximum)
    {
        var result = new StringBuilder();
        foreach (var rune in text.EnumerateRunes().Take(maximum))
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format) continue;
            if (Rune.IsControl(rune) || Rune.IsWhiteSpace(rune)) { result.Append(' '); continue; }
            var value = rune.ToString();
            if ("\\`*_~|<>[]#".Contains(value, StringComparison.Ordinal)) result.Append('\\');
            result.Append(value);
        }
        return result.ToString();
    }
}
