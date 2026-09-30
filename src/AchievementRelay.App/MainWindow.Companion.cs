using System.Windows;
using AchievementRelay.App.Services;

namespace AchievementRelay.App;

public partial class MainWindow
{
    private string? _nowPlayingArtworkKey;
    private async void RefreshNowPlaying()
    {
        var name = _services.SteamMonitorCoordinator.CurrentGameName;
        var game = _services.CompanionLibrary.Snapshot.Where(x => name is not null && x.Provider == "Steam" && x.Name == name).MaxBy(x => x.ObservedAt);
        var recent = _services.CompanionLibrary.Activity.Latest;
        var lastGame = recent is null ? null : _services.CompanionLibrary.Snapshot
            .Where(x => x.Provider == recent.Provider && x.Key.EndsWith(":" + recent.GameId, StringComparison.Ordinal))
            .MaxBy(x => x.ObservedAt);
        NowPlayingHeading.Text = name is not null ? "NOW PLAYING" : recent is not null ? "LAST PLAYED" : "NO ACTIVE GAME";
        NowPlayingTitle.Text = name ?? (recent is not null ? recent.Name + " · last played on " + recent.Provider : "Waiting for your next game");
        var shown = game ?? (name is null ? lastGame : null);
        var verifiedTotal = shown is not null && LibraryPresentation.HasVerifiedTotal(shown);
        NowPlayingProgressBar.Visibility = verifiedTotal ? Visibility.Visible : Visibility.Collapsed;
        NowPlayingProgressBar.Value = verifiedTotal ? LibraryPresentation.Percentage(shown!) : 0;
        RefreshRedlineSummary();
        var entries = _services.CompanionJournal.Snapshot.Where(x => !x.Achievement.IsHistorical && !x.Achievement.IsGameCompletion).ToArray();
        var latest = entries.MaxBy(x => x.ObservedAt);
        var count = latest is not null && DateTimeOffset.UtcNow - latest.ObservedAt < TimeSpan.FromMinutes(30)
            ? entries.Count(x => x.SessionId == latest.SessionId) : 0;
        NowPlayingProgress.Text = (shown is null ? "Progress appears after a verified game snapshot." :
            $"{shown.Earned}/{(verifiedTotal ? shown.Total?.ToString() : "?")} earned" + (verifiedTotal ? $" · {LibraryPresentation.Percentage(shown):0.#}% complete" : " · Total unverified")) +
            (name is not null ? $" · {count} unlocks this session" : recent is not null ? " · Saved progress; not live presence" : "");
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
