using System.Text.RegularExpressions;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public sealed partial class XboxNotificationClassifier
{
    private static readonly HashSet<string> XboxPackageFamilyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft.XboxGamingOverlay_8wekyb3d8bbwe",
        "Microsoft.GamingApp_8wekyb3d8bbwe",
        "Microsoft.XboxApp_8wekyb3d8bbwe",
        "Microsoft.Xbox.TCUI_8wekyb3d8bbwe",
        "Microsoft.GamingServices_8wekyb3d8bbwe"
    };

    private static readonly string[] AchievementPhrases =
    [
        "achievement unlocked",
        "rare achievement unlocked",
        "achievement earned",
        "succès déverrouillé",
        "succès obtenu",
        "erfolg freigeschaltet",
        "erfolg erzielt",
        "logro desbloqueado",
        "conquista desbloqueada",
        "conquista sbloccata",
        "prestatie ontgrendeld",
        "osiągnięcie odblokowane",
        "достижение разблокировано",
        "достижение получено",
        "実績を解除",
        "도전 과제 잠금 해제",
        "成就已解锁",
        "成就解鎖"
    ];

    public bool IsXboxSource(RawNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        return IsXboxSource(notification.PackageFamilyName);
    }

    public bool IsXboxSource(string? packageFamilyName)
    {
        return !string.IsNullOrWhiteSpace(packageFamilyName) &&
               XboxPackageFamilyNames.Contains(packageFamilyName.Trim());
    }

    public bool IsAchievement(RawNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!IsXboxSource(notification))
        {
            return false;
        }

        var content = string.Join(' ', notification.TextElements).Trim();
        if (content.Length == 0)
        {
            return false;
        }

        var normalized = content.ToLowerInvariant();
        return AchievementPhrases.Any(normalized.Contains) || GamerscorePattern().IsMatch(content);
    }

    public bool IsUnlockLabel(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return AchievementPhrases.Any(phrase => normalized.Contains(phrase, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:\+\s*)?\d{1,4}\s*(?:G|GS|Gamerscore)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GamerscorePattern();
}
