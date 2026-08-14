using System.Runtime.InteropServices;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace AchievementRelay.App.Services;

public enum NotificationAccessState
{
    Unknown,
    Allowed,
    Denied,
    Unspecified,
    Unavailable
}

public sealed class XboxNotificationListenerService(
    XboxNotificationClassifier classifier,
    ActivityLog activityLog) : IDisposable
{
    private readonly UserNotificationListener _listener = UserNotificationListener.Current;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly HashSet<uint> _knownNotificationIds = [];
    private bool _started;

    public event EventHandler<RawNotificationCapturedEventArgs>? XboxNotificationCaptured;

    public bool IsRunning => _started;

    public NotificationAccessState GetAccessState()
    {
        try
        {
            return MapAccessState(_listener.GetAccessStatus());
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            return NotificationAccessState.Unavailable;
        }
    }

    public async Task<NotificationAccessState> RequestAccessAsync()
    {
        try
        {
            var result = await _listener.RequestAccessAsync();
            var state = MapAccessState(result);
            activityLog.Info(state == NotificationAccessState.Allowed
                ? "Windows notification access granted."
                : "Windows notification access was not granted.");
            return state;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            activityLog.Error("Windows could not open the notification permission prompt. Install and launch the packaged app, then try again.");
            return NotificationAccessState.Unavailable;
        }
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return true;
        }

        if (GetAccessState() != NotificationAccessState.Allowed)
        {
            return false;
        }

        try
        {
            await PrimeKnownNotificationsAsync(cancellationToken);
            _listener.NotificationChanged += OnNotificationChanged;
            _started = true;
            activityLog.Success("Xbox notification monitoring is active.");
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or InvalidOperationException or COMException)
        {
            activityLog.Error($"Xbox notification monitoring could not start: {exception.Message}");
            return false;
        }
    }

    public async Task<int> RescanCurrentXboxNotificationsAsync(CancellationToken cancellationToken = default)
    {
        if (GetAccessState() != NotificationAccessState.Allowed)
        {
            return 0;
        }

        return await SyncNotificationsAsync(includeKnown: true, cancellationToken);
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _listener.NotificationChanged -= OnNotificationChanged;
        _started = false;
        activityLog.Info("Xbox notification monitoring stopped.");
    }

    public void Dispose()
    {
        Stop();
        _syncGate.Dispose();
    }

    private async Task PrimeKnownNotificationsAsync(CancellationToken cancellationToken)
    {
        var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellationToken);
        _knownNotificationIds.Clear();
        foreach (var notification in notifications)
        {
            _knownNotificationIds.Add(notification.Id);
        }
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        _ = SyncNotificationsAsync(includeKnown: false, CancellationToken.None);
    }

    private async Task<int> SyncNotificationsAsync(bool includeKnown, CancellationToken cancellationToken)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var notifications = await _listener.GetNotificationsAsync(NotificationKinds.Toast).AsTask(cancellationToken);
            var currentIds = notifications.Select(notification => notification.Id).ToHashSet();
            var captured = 0;

            foreach (var userNotification in notifications.OrderBy(notification => notification.CreationTime))
            {
                var isNew = _knownNotificationIds.Add(userNotification.Id);
                if (!isNew && !includeKnown)
                {
                    continue;
                }

                if (!TryGetSourceIdentity(userNotification, out var displayName, out var packageFamilyName) ||
                    !classifier.IsXboxSource(packageFamilyName))
                {
                    continue;
                }

                var rawNotification = ConvertNotification(userNotification, displayName, packageFamilyName);
                if (rawNotification is null)
                {
                    continue;
                }

                captured++;
                XboxNotificationCaptured?.Invoke(this, new RawNotificationCapturedEventArgs(rawNotification));
            }

            _knownNotificationIds.RemoveWhere(id => !currentIds.Contains(id));
            return captured;
        }
        catch (UnauthorizedAccessException)
        {
            activityLog.Warning("Windows notification access was revoked. Open Setup to grant it again.");
            return 0;
        }
        catch (Exception exception) when (exception is InvalidOperationException or COMException)
        {
            activityLog.Error($"Windows notification listener error: {exception.Message}");
            return 0;
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private static bool TryGetSourceIdentity(
        UserNotification notification,
        out string displayName,
        out string packageFamilyName)
    {
        try
        {
            displayName = notification.AppInfo.DisplayInfo.DisplayName ?? string.Empty;
            packageFamilyName = notification.AppInfo.PackageFamilyName ?? string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NullReferenceException)
        {
            displayName = string.Empty;
            packageFamilyName = string.Empty;
            return false;
        }
    }

    private static RawNotification? ConvertNotification(
        UserNotification notification,
        string displayName,
        string packageFamilyName)
    {
        try
        {
            var binding = notification.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            var textElements = binding?.GetTextElements()
                .Select(element => element.Text)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToArray() ?? [];

            return new RawNotification
            {
                PlatformId = notification.Id,
                ApplicationDisplayName = string.IsNullOrWhiteSpace(displayName) ? "Xbox" : displayName,
                PackageFamilyName = packageFamilyName,
                CreatedAt = notification.CreationTime,
                TextElements = textElements
            };
        }
        catch (Exception exception) when (exception is InvalidOperationException or NullReferenceException)
        {
            return null;
        }
    }

    private static NotificationAccessState MapAccessState(UserNotificationListenerAccessStatus status) => status switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccessState.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccessState.Denied,
        UserNotificationListenerAccessStatus.Unspecified => NotificationAccessState.Unspecified,
        _ => NotificationAccessState.Unknown
    };
}
