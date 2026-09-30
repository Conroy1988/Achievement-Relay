using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AchievementRelay.App;

public sealed partial class CompanionWindow
{
    private readonly ComboBox _soundPack = new() { ItemsSource = Enum.GetNames<UnlockSoundPack>() };
    private readonly ComboBox _rareSoundPack = new() { ItemsSource = Enum.GetNames<UnlockSoundPack>() };
    private readonly Slider _soundVolume = new() { Minimum = 0, Maximum = 100, TickFrequency = 5, IsSnapToTickEnabled = true };
    private readonly CheckBox _soundStudio = new() { Content = "Use Sound Studio volume and packs (master sound switch still applies)" };
    private readonly CheckBox _importHistory = new() { Content = "Import earned history from future validated game snapshots" };
    private readonly UnlockChime _soundPreview = new();
    private readonly CancellationTokenSource _showcaseCancellation = new();
    private readonly ListBox _libraryGames = new() { DisplayMemberPath = "Label", Height = 190 };
    private readonly ListBox _libraryHistory = new() { DisplayMemberPath = "Name", Height = 190 };
    private readonly TextBlock _libraryDetails = Text("Play a monitored game to populate your library.", 18);
    private readonly TextBlock _closest = Text("");
    private readonly TextBlock _quietStatus = Text("");
    private readonly TextBlock _performance = Text("");
    private readonly ItemsControl _sessionTimeline = new();
    private readonly System.Windows.Controls.TextBox _timelineSearch = new();
    private readonly TextBlock _timelineSummary = Text("");
    private readonly ListBox _trophies = new() { DisplayMemberPath = "Label", Height = 300 };
    private readonly System.Windows.Controls.Image _libraryArt = new() { Height = 165, Stretch = Stretch.UniformToFill };
    private int _libraryRevision = -1;
    private readonly System.Windows.Controls.TextBox _librarySearch = new();
    private readonly ComboBox _librarySort = new() { ItemsSource = new[] { "Recently observed", "Game name", "Closest to completion" }, SelectedIndex = 0 };
    private readonly ComboBox _libraryPlatform = new() { ItemsSource = new[] { "All platforms", "Steam", "Xbox" }, SelectedIndex = 0 };
    private readonly ComboBox _libraryCompletion = new() { ItemsSource = new[] { "All progress", "In progress", "Completed", "Total unknown" }, SelectedIndex = 0 };
    private readonly TextBlock _librarySummary = Text("");
    private readonly System.Windows.Controls.TextBox _historySearch = new();
    private readonly TextBlock _historySummary = Text("");
    private readonly System.Windows.Controls.ProgressBar _libraryProgress = new() { Maximum = 100, Height = 6, Margin = new Thickness(0, 8, 0, 12) };

    private sealed record GameRow(LibraryGame Game)
    {
        public string Label => $"{Game.Name} · {Game.Provider} · {Game.Earned}/{Game.Total?.ToString() ?? "?"}";
        public string ProgressLabel => LibraryPresentation.HasVerifiedTotal(Game)
            ? $"{Game.Provider}  ·  {Game.Earned} / {Game.Total} earned  ·  {LibraryPresentation.Percentage(Game):0.#}%"
            : $"{Game.Provider}  ·  {Game.Earned} earned  ·  Total unknown";
        public double Percentage => LibraryPresentation.Percentage(Game);
        public Visibility ProgressVisibility => LibraryPresentation.HasVerifiedTotal(Game) ? Visibility.Visible : Visibility.Collapsed;
        public string Status => LibraryPresentation.IsComplete(Game) ? "COMPLETE · VERIFIED SNAPSHOT" : "OBSERVED " + Game.ObservedAt.ToLocalTime().ToString("g");
    }
    private sealed record TimelineRow(JournalEntry Entry)
    {
        public string Time => Entry.ObservedAt.ToLocalTime().ToString("HH:mm");
        public string Context => $"{Entry.Achievement.GameName} · {Entry.Achievement.Platform ?? Entry.Achievement.SourceProvider}";
        public string Status => $"{RelayRarityClassifier.FormatPercentage(Entry.Achievement.RarityPercentage)} · {Entry.Delivery}";
    }
    private sealed record TrophyRow(AchievementEvent Achievement, bool Pinned, bool Imported)
    {
        public string Label => $"{(Pinned ? "★ " : "")}{Achievement.Name} · {Achievement.GameName}\n{RelayRarityClassifier.FormatPercentage(Achievement.RarityPercentage)} · {(Imported ? "Imported history" : Achievement.IsGameCompletion ? "100% completion" : "Live unlock")}";
    }

    private void BuildShowcaseTabs(TabControl tabs, StackPanel controls, StackPanel health)
    {
        _libraryGames.DisplayMemberPath = ""; _libraryGames.ItemTemplate = (DataTemplate)FindResource("LibraryRowTemplate");
        _libraryGames.ItemContainerStyle = (Style)FindResource("GalleryItemStyle");
        _trophies.DisplayMemberPath = ""; _trophies.ItemTemplate = (DataTemplate)FindResource("TrophyRowTemplate");
        _trophies.ItemContainerStyle = (Style)FindResource("GalleryItemStyle");
        var sound = Panel(); sound.Children.Add(Text("SOUND STUDIO", 22));
        sound.Children.Add(Text("Save presentation, sound and history applies presentation, sound and history controls together. The master sound switch in Settings also controls previews."));
        sound.Children.Add(ActionButton("Stop preview", () => _soundPreview.Dispose()));
        sound.Children.Add(_soundStudio); sound.Children.Add(Text("Standard unlock")); sound.Children.Add(_soundPack);
        sound.Children.Add(Text("Rare unlock")); sound.Children.Add(_rareSoundPack);
        sound.Children.Add(Text("Dedicated volume · 0–100%")); sound.Children.Add(_soundVolume); sound.Children.Add(SliderValue(_soundVolume, "Volume: {0:0}%"));
        sound.Children.Add(ActionButton("Preview standard sound", () => PreviewSound(false)));
        sound.Children.Add(ActionButton("Preview rare sound", () => PreviewSound(true)));
        sound.Children.Add(Text("QUIET GAMING", 20)); sound.Children.Add(_quietStatus);
        foreach (var option in new[] { ("Mute for 30 minutes", AchievementOverlayService.QuietMode.Mute), ("Hide alerts for 30 minutes", AchievementOverlayService.QuietMode.Hide), ("Hold up to 8 alerts for later", AchievementOverlayService.QuietMode.Hold), ("Resume local alerts", AchievementOverlayService.QuietMode.Off) })
            sound.Children.Add(ActionButton(option.Item1, () => { _services.AchievementOverlayService.SetQuiet(option.Item2, TimeSpan.FromMinutes(30)); RefreshShowcaseStatus(); }));
        sound.Children.Add(Text("Temporary controls reset when Relay closes. Discord delivery continues. Held alerts resume after 30 minutes or when you choose Resume; the queue is capped at eight."));
        AddTab(tabs, "Sound & quiet", sound);

        var library = Panel(); library.Children.Add(Text("YOUR GAME LIBRARY", 22));
        library.Children.Add(Text("Verified snapshots observed on this PC—not a complete account library. Unknown totals stay unknown. Last-observed counts can lag behind play."));
        var searchSort = new Grid(); searchSort.ColumnDefinitions.Add(new ColumnDefinition()); searchSort.ColumnDefinitions.Add(new ColumnDefinition());
        var searchColumn = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        searchColumn.Children.Add(Text("Find a game")); searchColumn.Children.Add(_librarySearch);
        var sortColumn = new StackPanel(); sortColumn.Children.Add(Text("Sort games")); sortColumn.Children.Add(_librarySort);
        Grid.SetColumn(sortColumn, 1); searchSort.Children.Add(searchColumn); searchSort.Children.Add(sortColumn); library.Children.Add(searchSort);
        var filters = new Grid(); filters.ColumnDefinitions.Add(new ColumnDefinition()); filters.ColumnDefinitions.Add(new ColumnDefinition());
        _libraryPlatform.Margin = new Thickness(0, 4, 8, 4); Grid.SetColumn(_libraryCompletion, 1);
        filters.Children.Add(_libraryPlatform); filters.Children.Add(_libraryCompletion); library.Children.Add(filters);
        library.Children.Add(_librarySummary); library.Children.Add(_libraryGames);
        library.Children.Add(_libraryArt); library.Children.Add(_libraryDetails); library.Children.Add(_libraryProgress);
        library.Children.Add(_closest);
        library.Children.Add(_importHistory);
        System.Windows.Automation.AutomationProperties.SetName(_librarySearch, "Search game library");
        System.Windows.Automation.AutomationProperties.SetName(_librarySort, "Sort game library");
        System.Windows.Automation.AutomationProperties.SetName(_libraryPlatform, "Filter library by platform");
        System.Windows.Automation.AutomationProperties.SetName(_libraryCompletion, "Filter library by completion");
        System.Windows.Automation.AutomationProperties.SetName(_historySearch, "Search selected game's imported achievements");
        System.Windows.Automation.AutomationProperties.SetName(_libraryProgress, "Selected game's verified completion percent");
        _librarySearch.TextChanged += (_, _) => { _libraryRevision = -1; RefreshLibrary(); };
        _librarySort.SelectionChanged += (_, _) => { _libraryRevision = -1; RefreshLibrary(); };
        _libraryPlatform.SelectionChanged += (_, _) => { _libraryRevision = -1; RefreshLibrary(); };
        _libraryCompletion.SelectionChanged += (_, _) => { _libraryRevision = -1; RefreshLibrary(); };
        _historySearch.TextChanged += (_, _) => RefreshLibraryHistory();
        library.Children.Add(ActionButton("Refresh library", () => { _libraryRevision = -1; RefreshLibrary(); }));
        library.Children.Add(Text("IMPORTED HISTORY · LOCAL ONLY", 16)); library.Children.Add(_historySearch); library.Children.Add(_historySummary); library.Children.Add(_libraryHistory);
        library.Children.Add(Text("Opt-in import captures up to 300 earned achievements per game / 3,000 overall as monitoring naturally fetches complete snapshots. It does not make extra Xbox API calls. Historical entries never enter the delivery queue."));
        library.Children.Add(ActionButton("Export historical poster", () => Run(() => ExportPosterAsync(_libraryHistory.SelectedItem as AchievementEvent))));
        library.Children.Add(ActionButton("Pin / unpin historical trophy", () => Run(() => TogglePinAsync(_libraryHistory.SelectedItem as AchievementEvent))));
        AddTab(tabs, "Library", library);
        _libraryGames.SelectionChanged += async (_, _) =>
        {
            try { await SelectLibraryGameAsync(); }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or InvalidOperationException)
            { _notice.Text = "Artwork is unavailable. Your game details are still shown."; }
        };

        var trophies = Panel(); trophies.Children.Add(Text("THE TROPHY ROOM", 24));
        trophies.Children.Add(Text("Pinned favourites first, then your rarest known unlocks. Unknown rarity is never treated as rare.")); trophies.Children.Add(_trophies);
        trophies.Children.Add(ActionButton("Refresh trophies", RefreshTrophies));
        trophies.Children.Add(ActionButton("Pin / unpin selected", () => Run(() => TogglePinAsync((_trophies.SelectedItem as TrophyRow)?.Achievement))));
        trophies.Children.Add(ActionButton("Export selected poster", () => Run(() => ExportPosterAsync((_trophies.SelectedItem as TrophyRow)?.Achievement))));
        AddTab(tabs, "Trophies", trophies);

        var release = Panel(); release.Children.Add(Text("WHAT’S NEW · SHOWCASE", 23));
        release.Children.Add(Text("Cinematic beam reveals · original sound packs · game library · trophy room · local posters · quiet controls. Explore the new presentation without sending anything to Discord."));
        foreach (var example in new[] { ("Standard unlock", 55d, false), ("Rare unlock", 3d, false), ("Platinum unlock", .2, false), ("Game complete", .2, true) })
        {
            var sample = new AchievementEvent { Id = "tour", Name = example.Item1, GameName = "Achievement Relay", SourceProvider = "Preview", RarityKnown = true,
                RarityPercentage = example.Item2, IsRare = example.Item2 < 10, IsGameCompletion = example.Item3, VerifiedAchievementTotal = example.Item3 ? 50 : null };
            release.Children.Add(new System.Windows.Controls.Image { Source = MainWindow.DecodeRedlineImage(AchievementOverlayWindow.RenderPreview(AchievementOverlayPresentation.Create(sample)), 520), Height = 76, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left });
            release.Children.Add(ActionButton("Try " + example.Item1, () => Run(async () =>
            {
                var settings = await _services.SettingsStore.LoadAsync();
                _services.AchievementOverlayService.Preview(new AchievementEvent
                { Id = "preview", Name = example.Item1, GameName = "Achievement Relay", SourceProvider = "Preview", RarityKnown = true,
                    RarityPercentage = example.Item2, IsRare = example.Item2 < 10, IsGameCompletion = example.Item3, VerifiedAchievementTotal = example.Item3 ? 50 : null }, settings: settings);
                _notice.Text = "Sample queued locally. Nothing sent to Discord.";
            })));
        }
        release.Children.Add(ActionButton("View published release notes", _updates));
        release.Children.Add(Text("BORDERLESS & MULTI-MONITOR PLAY", 20));
        release.Children.Add(Text("Use Borderless / Windowed Fullscreen in your game for desktop overlays. Exclusive fullscreen can bypass the Windows compositor and hide Relay; Relay does not inject into games or bypass anti-cheat. The strip remains click-through and never takes keyboard focus. Display/DPI changes recalculate its safe position. Choose a fixed screen if alt-tab would move it to the wrong display."));
        release.Children.Add(ActionButton("Test overlay on your chosen display", ReplayControls));
        AddTab(tabs, "Release tour", release);
        health.Children.Add(Text("PERFORMANCE SNAPSHOT", 19)); health.Children.Add(_performance);
        health.Children.Add(ActionButton("Export private-safe support report", ExportSupport));
        controls.Children.Add(Text("Editor: drag to position, use the size slider or mouse wheel over the strip to resize. Edges and centre snap within 4%. The real overlay stays inside the screen work area."));
        var editorSample = new AchievementEvent { Id = "editor", Name = "Your next achievement", GameName = "Achievement Relay", SourceProvider = "Preview", RarityPercentage = .5, IsRare = true, RarityKnown = true };
        _strip.Child = new System.Windows.Controls.Image { Source = MainWindow.DecodeRedlineImage(AchievementOverlayWindow.RenderPreview(AchievementOverlayPresentation.Create(editorSample)), 520), Stretch = Stretch.Fill };
        _monitor.SelectionChanged += (_, _) =>
        {
            var screen = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(x => x.DeviceName == (_monitor.SelectedItem as MonitorRow)?.Id)
                ?? System.Windows.Forms.Screen.PrimaryScreen;
            if (screen is not null) _position.Height = Math.Clamp(_position.ActualWidth * screen.Bounds.Height / Math.Max(1d, screen.Bounds.Width), 150, 300);
        };
        _scale.ValueChanged += (_, _) => PositionStrip();
        _position.MouseWheel += (_, e) => { _scale.Value = Math.Clamp(_scale.Value + Math.Sign(e.Delta) * .05, .75, 1.5); e.Handled = true; };
        _position.MouseLeftButtonUp += (_, _) => { _x = Snap(_x); _y = Snap(_y); PositionStrip(); };
        _sessions.SelectionChanged += (_, _) => RefreshSessionTimeline();
        var sessionsTab = (TabItem)tabs.Items[1];
        _sessionTimeline.ItemTemplate = (DataTemplate)FindResource("TimelineRowTemplate");
        System.Windows.Automation.AutomationProperties.SetName(_timelineSearch, "Search selected session by game or achievement");
        _timelineSearch.TextChanged += (_, _) => RefreshSessionTimeline();
        if (sessionsTab.Content is ScrollViewer { Content: StackPanel session }) {
            session.Children.Add(Text("UNLOCK TIMELINE", 18)); session.Children.Add(Text("Times show when this PC observed each unlock. Filtering this view does not change the shared recap."));
            session.Children.Add(_timelineSearch); session.Children.Add(_timelineSummary); session.Children.Add(_sessionTimeline);
            session.Children.Add(Text("100% celebrations use verified provider totals and a live final unlock on Steam or Xbox. Missing totals remain unknown. Completion proof survives delivery retries."));
        }
        RefreshLibrary(); RefreshTrophies(); RefreshShowcaseStatus();
    }
    private void PreviewSound(bool rare) => Run(async () => {
        var current = await _services.SettingsStore.LoadAsync();
        _soundPreview.Dispose();
        if (!current.AchievementOverlaySoundEnabled) { _notice.Text = "Sound is muted in Settings. Enable the master sound switch to hear a preview."; return; }
        _soundPreview.Play((int)_soundVolume.Value, rare ? RelayRarityTier.Platinum : RelayRarityTier.Unranked,
            (UnlockSoundPack)Math.Max(0, rare ? _rareSoundPack.SelectedIndex : _soundPack.SelectedIndex));
        _notice.Text = _soundVolume.Value <= 0 ? "Preview is silent because volume is 0%." : "Playing a local preview. Use Stop preview to end it.";
    });
    private static double Snap(double value) => new[] { 0d, .5, 1d }.FirstOrDefault(x => Math.Abs(value - x) < .04, value);
    private void RefreshLibrary()
    {
        var games = _services.CompanionLibrary.Snapshot;
        var hash = new HashCode();
        foreach (var game in games) hash.Add(game);
        var revision = hash.ToHashCode();
        if (_libraryRevision == revision) return; _libraryRevision = revision;
        var key = (_libraryGames.SelectedItem as GameRow)?.Game.Key;
        var ordered = LibraryPresentation.Filter(games, _librarySearch.Text,
            _libraryPlatform.SelectedIndex > 0 ? _libraryPlatform.SelectedItem as string : null,
            _libraryCompletion.SelectedIndex, _librarySort.SelectedIndex);
        var rows = ordered.Select(x => new GameRow(x)).ToArray();
        _librarySummary.Text = $"{rows.Length} of {games.Length} games · {games.Count(LibraryPresentation.IsComplete)} completed in verified snapshots";
        if (rows.Length == 0) { _libraryDetails.Text = "No matching games. Clear your filters or play a monitored game to build your library."; _libraryHistory.ItemsSource = null; _libraryArt.Source = null; _libraryArt.Visibility = Visibility.Collapsed; _libraryProgress.Visibility = Visibility.Collapsed; }
        _libraryGames.Height = Math.Clamp(rows.Length * 120, 120, 300);
        _libraryGames.ItemsSource = rows; _libraryGames.SelectedItem = rows.FirstOrDefault(x => x.Game.Key == key) ?? rows.FirstOrDefault();
        RefreshLibraryHistory();
        var close = ordered.Where(x => LibraryPresentation.HasVerifiedTotal(x) && x.Total > x.Earned).OrderBy(x => x.Total - x.Earned).Take(3);
        _closest.Text = "CLOSEST TO COMPLETION\n" + string.Join("\n", close.Select(x => $"{x.Name} · {x.Total - x.Earned} remaining"));
        if (!close.Any()) _closest.Text += "No matching incomplete games with verified totals yet.";
    }
    private async Task SelectLibraryGameAsync()
    {
        if (_libraryGames.SelectedItem is not GameRow row) return;
        var game = row.Game;
        _libraryDetails.Text = $"{game.Name}\n{row.ProgressLabel}\nSnapshot observed {game.ObservedAt.ToLocalTime():g} · not a last-played timestamp";
        _libraryProgress.Value = row.Percentage; _libraryProgress.Visibility = row.ProgressVisibility;
        RefreshLibraryHistory();
        _libraryArt.Source = null; _libraryArt.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(game.Artwork)) return;
        var art = await _services.ArtworkClient.GetAsync(new AchievementEvent { Id = "library", Name = game.Name, GameName = game.Name, SourceProvider = game.Provider, HeroImageUrl = game.Artwork }, _showcaseCancellation.Token);
        if (!_closed && (_libraryGames.SelectedItem as GameRow)?.Game.Key == game.Key) {
            _libraryArt.Source = MainWindow.DecodeRedlineImage(art.HeroImageBytes, 850);
            _libraryArt.Visibility = _libraryArt.Source is null ? Visibility.Collapsed : Visibility.Visible;
        }
    }
    private void RefreshLibraryHistory()
    {
        var history = (_libraryGames.SelectedItem as GameRow)?.Game.History ?? [];
        var selected = (_libraryHistory.SelectedItem as AchievementEvent)?.Id;
        var rows = history.Where(x => x.Name.Contains(_historySearch.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.UnlockedAt).ToArray();
        _libraryHistory.ItemsSource = rows;
        _libraryHistory.SelectedItem = rows.FirstOrDefault(x => x.Id == selected) ?? rows.FirstOrDefault();
        _historySummary.Text = history.Length == 0 ? "No imported history for this selection. Live unlocks remain in Gallery."
            : $"{rows.Length} of {history.Length} imported achievements · never reposted";
    }
    private void RefreshTrophies()
    {
        var pins = _settings.Companion.PinnedAchievements ?? [];
        var live = _services.CompanionJournal.Snapshot.Select(x => new TrophyRow(x.Achievement, pins.Contains(x.Achievement.Id), x.Achievement.IsHistorical));
        var historical = _services.CompanionLibrary.Snapshot.SelectMany(x => x.History).Select(x => new TrophyRow(x, pins.Contains(x.Id), true));
        _trophies.ItemsSource = live.Concat(historical).DistinctBy(x => x.Achievement.Id).OrderByDescending(x => x.Pinned)
            .ThenBy(x => x.Achievement.RarityPercentage is >= 0 and <= 100 ? x.Achievement.RarityPercentage : double.MaxValue).Take(300).ToArray();
        _trophies.Height = Math.Clamp(_trophies.Items.Count * 85, 100, 400);
    }
    private async Task TogglePinAsync(AchievementEvent? achievement)
    {
        if (achievement is null) { _notice.Text = "Select an achievement first."; return; }
        var pins = (_settings.Companion.PinnedAchievements ?? []).ToHashSet();
        if (!pins.Remove(achievement.Id)) pins.Add(achievement.Id);
        await SaveAsync(_settings.Companion with { PinnedAchievements = pins.Take(100).ToArray() }); RefreshTrophies();
    }
    private async Task ExportPosterAsync(AchievementEvent? achievement)
    {
        if (achievement is null) { _notice.Text = "Select an achievement first."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG image|*.png", FileName = "Achievement-Relay-Poster.png", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        var post = await _services.AchievementPostComposer.ComposeAsync(achievement, _settings with { Companion = _settings.Companion with { DiscordPresentation = DiscordPresentation.Showcase } });
        if (!post.UsesCollectorCard || post.AttachmentBytes is not { Length: > 0 }) { _notice.Text = "The poster could not be rendered. No file was written."; return; }
        await File.WriteAllBytesAsync(dialog.FileName, post.AttachmentBytes); _notice.Text = "Poster saved locally. Nothing sent to Discord.";
    }
    private void RefreshSessionTimeline()
    {
        var entries = SessionEntries;
        var rows = entries.Where(x => string.Concat(x.Achievement.GameName, " ", x.Achievement.Name)
            .Contains(_timelineSearch.Text.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ObservedAt).Select(x => new TimelineRow(x)).ToArray();
        _sessionTimeline.ItemsSource = rows;
        _timelineSummary.Text = entries.Length == 0 ? "No recorded session selected."
            : $"{rows.Length} of {entries.Length} recorded events in this session";
    }
    private void RefreshShowcaseStatus()
    {
        _quietStatus.Text = _services.AchievementOverlayService.QuietStatus;
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        _performance.Text = $"Working set: {process.WorkingSet64 / 1048576d:0} MB · Total CPU time: {process.TotalProcessorTime.TotalSeconds:0.0}s\nArtwork cache: {_services.ArtworkClient.CachedBytes / 1048576d:0.0} / 24 MB · bounded to 32 items / 15 minutes\nLibrary: {_services.CompanionLibrary.Snapshot.Length} games\n{_services.CompanionLibrary.Error}";
    }
    private void ExportSupport()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "JSON report|*.json", FileName = "Relay-Support.json" };
        if (dialog.ShowDialog(this) != true) return;
        Run(async () =>
        {
            // Allowlist only: no logs, error strings, paths, URLs, names or account identifiers.
            var report = new { version = typeof(CompanionWindow).Assembly.GetName().Version?.ToString(), os = Environment.OSVersion.Version.ToString(),
                generatedUtc = DateTimeOffset.UtcNow, xboxRunning = _services.RelayCoordinator.IsRunning, steamRunning = _services.SteamMonitorCoordinator.IsRunning,
                steamPhase = _services.SteamMonitorCoordinator.Phase.ToString(), updateStage = _services.UpdateService.Snapshot.Stage.ToString(),
                journalCount = _services.CompanionJournal.Snapshot.Length, libraryCount = _services.CompanionLibrary.Snapshot.Length,
                historyStorageIssue = _services.CompanionJournal.StorageError is not null, libraryStorageIssue = _services.CompanionLibrary.Error is not null,
                artworkCacheBytes = _services.ArtworkClient.CachedBytes, animation = _settings.AchievementOverlayAnimationEnabled, reducedMotion = _settings.AchievementOverlayReducedMotion };
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            _notice.Text = "Support report saved. It excludes credentials, identifiers, game/player names, paths and raw logs.";
        });
    }
}
