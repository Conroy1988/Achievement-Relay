using System.ComponentModel;
using System.Windows;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App;

public partial class MainWindow
{
    private AppSettings ReadOverlayPreferences() => _settings with
    {
        AchievementOverlayAnimationEnabled = SettingsOverlayAnimationCheckBox.IsChecked == true,
        AchievementOverlayReducedMotion = SettingsOverlayReducedMotionCheckBox.IsChecked == true,
        AchievementOverlayFollowWindowsMotion = SettingsOverlayFollowWindowsCheckBox.IsChecked == true,
        AchievementOverlaySoundEnabled = SettingsOverlaySoundCheckBox.IsChecked == true,
        AchievementOverlayVolume = (int)SettingsOverlayVolumeSlider.Value
    };

    private void RefreshOverlayMotionStatus()
    {
        if (OverlayMotionStatusText is null || _settings is null) return;
        OverlayMotionStatusText.Text = OverlayMotionPolicy.Describe(ReadOverlayPreferences(),
            SystemParameters.ClientAreaAnimation, SystemParameters.HighContrast);
    }

    private void OverlayMotionPreference_Click(object sender, RoutedEventArgs e) => RefreshOverlayMotionStatus();

    private void OnOverlayMotionSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isExiting || Dispatcher.HasShutdownStarted) return;
        if (e.PropertyName is not null and not nameof(SystemParameters.ClientAreaAnimation) and not nameof(SystemParameters.HighContrast)) return;
        try { _ = Dispatcher.InvokeAsync(() => { if (!_isExiting) RefreshOverlayMotionStatus(); }); }
        catch (InvalidOperationException) when (Dispatcher.HasShutdownStarted) { }
    }
}
