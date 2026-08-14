using System.Security.Cryptography;
using System.Text;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static class NotificationFingerprint
{
    public static string Create(RawNotification notification, string achievementName, int? gamerscore)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var source = string.Join('|',
            notification.PackageFamilyName,
            notification.PlatformId,
            notification.CreatedAt.ToUniversalTime().ToString("O"),
            achievementName.Trim().ToUpperInvariant(),
            gamerscore?.ToString() ?? string.Empty);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))).ToLowerInvariant();
    }
}
