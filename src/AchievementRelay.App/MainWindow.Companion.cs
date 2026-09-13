using System.Windows;

namespace AchievementRelay.App;

public partial class MainWindow
{
    private string? _nowPlayingArtworkKey;
    private async void RefreshNowPlaying()
    {
        var name = _services.SteamMonitorCoordinator.CurrentGameName;
        var game = _services.CompanionLibrary.Snapshot.Where(x => name is not null && x.Provider == "Steam" && x.Name == name).MaxBy(x => x.ObservedAt);
        var latestXbox = _services.CompanionLibrary.Snapshot.Where(x => x.Provider == "Xbox").MaxBy(x => x.ObservedAt);
        NowPlayingTitle.Text = name ?? (latestXbox is not null ? latestXbox.Name + " · last observed on Xbox" : "Waiting for your next game");
        var shown = game ?? (name is null ? latestXbox : null);
        var entries = _services.CompanionJournal.Snapshot;
        var latest = entries.MaxBy(x => x.ObservedAt);
        var count = latest is not null && DateTimeOffset.UtcNow - latest.ObservedAt < TimeSpan.FromMinutes(30)
            ? entries.Count(x => x.SessionId == latest.SessionId) : 0;
        NowPlayingProgress.Text = (shown is null ? "Progress appears after a verified game snapshot." :
            $"{shown.Earned}/{shown.Total?.ToString() ?? "?"} earned" + (shown.Total is > 0 ? $" · {100d * shown.Earned / shown.Total:0.#}% complete" : "")) + $" · {count} unlocks this session";
        var artKey = shown?.Artwork;
        if (_nowPlayingArtworkKey == artKey) return;
        _nowPlayingArtworkKey = artKey; NowPlayingArtwork.Source = null;
        if (artKey is null) return;
        try
        {
            var art = await _services.ArtworkClient.GetAsync(new AchievementRelay.Core.Models.AchievementEvent
                { Id = "home-preview", Name = shown!.Name, SourceProvider = shown.Provider, HeroImageUrl = artKey });
            if (!_isExiting && _nowPlayingArtworkKey == artKey) NowPlayingArtwork.Source = DecodeRedlineImage(art.HeroImageBytes, 900);
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.Net.Http.HttpRequestException or System.IO.IOException) { }
    }
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
