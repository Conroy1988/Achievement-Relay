using System.Globalization;
using System.Text.RegularExpressions;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public sealed partial class AchievementNotificationParser(XboxNotificationClassifier classifier)
{
    private static readonly string[] RareWords =
    [
        "rare", "raro", "rara", "selten", "rarement", "raro", "редкое", "希少", "희귀", "稀有"
    ];

    public AchievementEvent? Parse(RawNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!classifier.IsAchievement(notification))
        {
            return null;
        }

        var lines = notification.TextElements
            .SelectMany(SplitLines)
            .Select(NormalizeWhitespace)
            .Where(static line => line.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (lines.Length == 0)
        {
            return null;
        }

        var gamerscore = FindGamerscore(lines);
        var isRare = lines.Any(line => RareWords.Any(word =>
            line.Contains(word, StringComparison.OrdinalIgnoreCase)));

        var descriptiveLines = lines
            .Where(line => !classifier.IsUnlockLabel(line))
            .Where(line => !IsOnlyGamerscore(line))
            .ToList();

        var name = descriptiveLines.FirstOrDefault() ?? "Xbox achievement unlocked";
        descriptiveLines.Remove(name);

        var gameName = ExtractGameName(descriptiveLines);
        if (gameName is not null)
        {
            descriptiveLines.Remove(gameName);
        }

        var description = descriptiveLines.Count == 0
            ? null
            : string.Join(Environment.NewLine, descriptiveLines);

        var fingerprint = NotificationFingerprint.Create(notification, name, gamerscore);

        return new AchievementEvent
        {
            Id = fingerprint,
            Name = name,
            Description = description,
            GameName = StripGamePrefix(gameName),
            Gamerscore = gamerscore,
            IsRare = isRare,
            ImageUrl = IsHttpUrl(notification.ImageUrl) ? notification.ImageUrl : null,
            SourceApplication = notification.ApplicationDisplayName,
            SourcePackageFamilyName = notification.PackageFamilyName,
            UnlockedAt = notification.CreatedAt,
            RawTextElements = lines
        };
    }

    private static IEnumerable<string> SplitLines(string value) =>
        value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string NormalizeWhitespace(string value) => WhitespacePattern().Replace(value.Trim(), " ");

    private static int? FindGamerscore(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var match = GamerscorePattern().Match(line);
            if (match.Success &&
                int.TryParse(match.Groups["score"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var score))
            {
                return score;
            }
        }

        return null;
    }

    private static bool IsOnlyGamerscore(string value) => GamerscoreOnlyPattern().IsMatch(value.Trim());

    private static string? ExtractGameName(IEnumerable<string> lines) =>
        lines.FirstOrDefault(line =>
            line.StartsWith("Game:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("Title:", StringComparison.OrdinalIgnoreCase) ||
            line.StartsWith("In ", StringComparison.OrdinalIgnoreCase));

    private static string? StripGamePrefix(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var separator = value.IndexOf(':');
        if (separator >= 0 && separator < value.Length - 1)
        {
            return value[(separator + 1)..].Trim();
        }

        return value.StartsWith("In ", StringComparison.OrdinalIgnoreCase)
            ? value[3..].Trim()
            : value;
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\+\s*)?(?<score>\d{1,4})\s*(?:G|GS|Gamerscore)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GamerscorePattern();

    [GeneratedRegex(@"^(?:\+\s*)?\d{1,4}\s*(?:G|GS|Gamerscore)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GamerscoreOnlyPattern();
}
