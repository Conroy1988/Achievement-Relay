using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed class RawNotificationCapturedEventArgs(RawNotification notification) : EventArgs
{
    public RawNotification Notification { get; } = notification;
}
