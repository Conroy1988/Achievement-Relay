using System.Collections.Concurrent;
using System.Windows.Threading;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed class AchievementOverlayService : IDisposable
{
    public const int MaximumQueuedNotifications = 8;

    private readonly ConcurrentQueue<QueuedOverlay> _queue = new();
    private readonly HashSet<string> _pendingEventIds = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Dispatcher _dispatcher;
    private readonly ActivityLog _activityLog;
    private CancellationTokenSource? _activePresentationCancellation;
    private bool _drainScheduled;
    private bool _queueLimitLogged;
    private bool _disposed;
    private int _queuedCount;

    public AchievementOverlayService(ActivityLog activityLog)
    {
        _activityLog = activityLog ?? throw new ArgumentNullException(nameof(activityLog));
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public bool Enqueue(
        AchievementEvent achievement,
        AppSettings settings,
        byte[]? achievementIconBytes = null)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.AchievementOverlayEnabled)
        {
            return false;
        }

        return EnqueueCore(
            achievement.Id,
            AchievementOverlayPresentation.Create(achievement, achievementIconBytes));
    }

    public bool Preview(AchievementEvent achievement, byte[]? achievementIconBytes = null)
    {
        ArgumentNullException.ThrowIfNull(achievement);
        return EnqueueCore(
            string.Concat("preview:", Guid.NewGuid().ToString("N")),
            AchievementOverlayPresentation.Create(achievement, achievementIconBytes));
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            ClearQueuedLocked();
            try
            {
                _activePresentationCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The active presentation completed between acquiring its
                // reference and cancelling it.
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _lifetimeCancellation.Cancel();
            ClearQueuedLocked();
        }
    }

    private bool EnqueueCore(string eventId, AchievementOverlayPresentation presentation)
    {
        lock (_gate)
        {
            if (_disposed ||
                _dispatcher.HasShutdownStarted ||
                _dispatcher.HasShutdownFinished ||
                _pendingEventIds.Contains(eventId))
            {
                return false;
            }

            if (_queuedCount >= MaximumQueuedNotifications)
            {
                if (!_queueLimitLogged)
                {
                    _queueLimitLogged = true;
                    _activityLog.Warning(
                        "The Signal Strip queue reached its safety limit. Discord delivery continued normally without extending the in-game overlay backlog.");
                }

                return false;
            }

            _pendingEventIds.Add(eventId);
            _queue.Enqueue(new QueuedOverlay(eventId, presentation));
            _queuedCount++;
            return ScheduleDrainLocked();
        }
    }

    private bool ScheduleDrainLocked()
    {
        if (_drainScheduled || _disposed)
        {
            return !_disposed;
        }

        try
        {
            _drainScheduled = true;
            _dispatcher.BeginInvoke(new Action(DrainQueueOnDispatcher));
            return true;
        }
        catch (InvalidOperationException)
        {
            _drainScheduled = false;
            ClearQueuedLocked();
            return false;
        }
    }

    private async void DrainQueueOnDispatcher()
    {
        try
        {
            while (!_lifetimeCancellation.IsCancellationRequested)
            {
                QueuedOverlay? queued = null;
                CancellationTokenSource? presentationCancellation = null;
                try
                {
                    lock (_gate)
                    {
                        if (!_queue.TryDequeue(out queued))
                        {
                            break;
                        }

                        _queuedCount--;
                        presentationCancellation =
                            CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
                        _activePresentationCancellation = presentationCancellation;
                    }

                    var window = new AchievementOverlayWindow(queued.Presentation);
                    await window.ShowForAsync(presentationCancellation.Token);
                }
                catch (OperationCanceledException) when (presentationCancellation?.IsCancellationRequested == true)
                {
                    if (_lifetimeCancellation.IsCancellationRequested)
                    {
                        return;
                    }
                }
                catch (Exception)
                {
                    if (queued is not null)
                    {
                        _activityLog.Warning(
                            $"The Signal Strip could not display {queued.Presentation.AchievementName}; Discord delivery was not affected.");
                    }
                }
                finally
                {
                    lock (_gate)
                    {
                        if (queued is not null)
                        {
                            _pendingEventIds.Remove(queued.EventId);
                        }

                        if (ReferenceEquals(_activePresentationCancellation, presentationCancellation))
                        {
                            _activePresentationCancellation = null;
                        }
                    }

                    presentationCancellation?.Dispose();
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _drainScheduled = false;
                if (_queue.IsEmpty)
                {
                    _queueLimitLogged = false;
                }
                if (!_disposed && !_queue.IsEmpty)
                {
                    ScheduleDrainLocked();
                }
            }
        }
    }

    private void ClearQueuedLocked()
    {
        while (_queue.TryDequeue(out var queued))
        {
            _pendingEventIds.Remove(queued.EventId);
        }

        _queuedCount = 0;
        _queueLimitLogged = false;
    }

    private sealed record QueuedOverlay(
        string EventId,
        AchievementOverlayPresentation Presentation);
}
