using System.Windows;

namespace AchievementRelay.App;

public partial class MainWindow
{
    private CompanionWindow? _companion;
    private void ShowCompanion_Click(object sender, RoutedEventArgs e)
    {
        if (_companion is not null) { _companion.Show(); _companion.Activate(); return; }
        _companion = new CompanionWindow(_services, _settings,
            preferences => _settings = _settings with { Companion = preferences },
            () => { _companion?.Hide(); NavigateTo(1); Activate(); },
            () => { _companion?.Hide(); NavigateTo(4); Activate(); },
            () => CopySupportSummary_Click(this, new RoutedEventArgs())) { Owner = this };
        _companion.Closed += (_, _) => _companion = null;
        _companion.Show();
    }
}
