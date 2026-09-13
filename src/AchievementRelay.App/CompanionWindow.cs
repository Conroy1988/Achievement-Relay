using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace AchievementRelay.App;

public sealed class CompanionWindow : Window
{
    private readonly AppServices _services;
    private AppSettings _settings;
    private readonly Action<CompanionPreferences> _saved;
    private readonly Action _connections;
    private readonly Action _updates;
    private readonly Action _support;
    private readonly ListBox _gallery = new() { MinHeight = 150, DisplayMemberPath = "Label" };
    private readonly TextBox _search = new();
    private readonly ComboBox _platform = new() { ItemsSource = new[] { "All platforms", "Steam", "Xbox" }, SelectedIndex = 0 };
    private readonly ComboBox _period = new() { ItemsSource = new[] { "All dates", "Today", "Last 7 days", "Last 30 days", "Rare unlocks", "Needs attention" }, SelectedIndex = 0 };
    private readonly TextBlock _details = Text("Select an achievement to inspect it.", 16);
    private readonly System.Windows.Controls.Image _artwork = new() { Height = 220, Stretch = Stretch.Uniform };
    private readonly TextBlock _notice = Text("");
    private readonly TextBlock _health = Text("");
    private readonly TextBlock _recap = Text("Play a game to start your first session.", 18);
    private readonly ComboBox _sessions = new() { DisplayMemberPath = "Label" };
    private readonly Slider _scale = new() { Minimum = .75, Maximum = 1.5, TickFrequency = .05, IsSnapToTickEnabled = true };
    private readonly Slider _seconds = new() { Minimum = 3, Maximum = 12, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly ComboBox _monitor = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _presentation = new() { ItemsSource = new[] { "Full artwork showcase", "Compact achievement card" } };
    private readonly CheckBox _rarity = new() { Content = "Distinct sounds and celebrations for rare unlocks" };
    private readonly TextBox _shared = new();
    private readonly Canvas _position = new() { Height = 150, Background = new SolidColorBrush(Color.FromRgb(12, 15, 20)), ClipToBounds = true };
    private readonly Border _strip = new() { Width = 130, Height = 24, Background = new SolidColorBrush(Color.FromRgb(185, 0, 34)), CornerRadius = new CornerRadius(4), Child = Text("SIGNAL STRIP", 10) };
    private readonly CheckBox _muteGame = new() { Content = "Mute this game's unlock chime" };
    private readonly CheckBox _hideGame = new() { Content = "Hide this game's overlay" };
    private readonly CheckBox _rareGame = new() { Content = "Celebrate rare unlocks only" };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(5) };
    private CancellationTokenSource? _artworkCancellation;
    private double _x, _y;
    private bool _closed;
    private bool _busy;
    private bool _refreshing;
    private string? _lastGame;

    public CompanionWindow(AppServices services, AppSettings settings, Action<CompanionPreferences> saved,
        Action connections, Action updates, Action support)
    {
        _services = services; _settings = settings; _saved = saved;
        _connections = connections; _updates = updates; _support = support;
        Title = "Achievement Relay · Companion"; Width = 1020; Height = 820; MinWidth = 760; MinHeight = 620;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AchievementRelay.App;component/CompanionStyles.xaml", UriKind.Relative) });
        Background = (Brush)FindResource("BackgroundBrush"); Foreground = (Brush)FindResource("TextBrush");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(22) };
        var heading = Text("YOUR ACHIEVEMENTS. YOUR WAY.", 25);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(_notice, Dock.Bottom); root.Children.Add(_notice);
        var tabs = new TabControl(); root.Children.Add(tabs); Content = root;
        var gallery = Panel();
        gallery.Children.Add(Text("GALLERY", 20));
        gallery.Children.Add(Text("Recent live unlocks retained on this PC · search by game or achievement. Latest 300 entries."));
        gallery.Children.Add(_search); gallery.Children.Add(_platform); gallery.Children.Add(_period);
        _gallery.Height = 200; gallery.Children.Add(_gallery); gallery.Children.Add(_artwork); gallery.Children.Add(_details);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Replay locally", Replay));
        actions.Children.Add(ActionButton("Retry pending delivery", () => Run(RetryAsync)));
        actions.Children.Add(ActionButton("Preview Discord card", () => Run(PreviewCardAsync)));
        actions.Children.Add(ActionButton("Confirm uncertain delivery", () => Run(ConfirmUncertainAsync)));
        gallery.Children.Add(actions);
        gallery.Children.Add(Text("PER-GAME CONTROLS", 16)); gallery.Children.Add(_muteGame); gallery.Children.Add(_hideGame); gallery.Children.Add(_rareGame);
        gallery.Children.Add(ActionButton("Save this game's preferences", () => Run(SaveGameAsync)));
        AddTab(tabs, "Gallery", gallery);
        var session = Panel(); session.Children.Add(Text("SESSION RECAP", 22)); session.Children.Add(Text("A new session begins after 30 minutes without a recorded unlock. Counts cover unlocks observed by this PC."));
        session.Children.Add(_sessions); session.Children.Add(_recap);
        session.Children.Add(ActionButton("Share this recap to Discord", () => Run(ShareRecapAsync)));
        session.Children.Add(Text("100% celebrations use verified provider totals. Steam completion is detected on the final eligible live unlock; Xbox completion remains unavailable when no total is supplied."));
        AddTab(tabs, "Sessions", session);
        var controls = Panel(); controls.Children.Add(Text("MAKE THE SIGNAL STRIP YOURS", 22));
        controls.Children.Add(Text("Screen")); controls.Children.Add(_monitor);
        controls.Children.Add(Text("Drag the strip to position it on the selected screen.")); controls.Children.Add(_position); _position.Children.Add(_strip);
        var positions = new WrapPanel();
        foreach (var preset in new[] { ("Top left", 0d, 0d), ("Top centre", .5, 0d), ("Top right", 1d, 0d), ("Bottom left", 0d, 1d), ("Bottom right", 1d, 1d) })
            positions.Children.Add(ActionButton(preset.Item1, () => { _x = preset.Item2; _y = preset.Item3; PositionStrip(); }));
        controls.Children.Add(positions);
        controls.Children.Add(Text("Size · 75%–150%")); controls.Children.Add(_scale);
        controls.Children.Add(Text("Display time · 3–12 seconds")); controls.Children.Add(_seconds); controls.Children.Add(_rarity);
        controls.Children.Add(Text("Discord appearance")); controls.Children.Add(_presentation);
        controls.Children.Add(ActionButton("Test unsaved controls locally", ReplayControls));
        controls.Children.Add(Text("CROSS-PC DELIVERY", 18));
        controls.Children.Add(Text(@"Optional: enter the same Windows network folder on both PCs, for example \\server\Relay. The folder must already exist and be writable. OneDrive and other sync folders are not supported. Both PCs must use this option for the same webhook. If the share is unavailable, posting waits. An interrupted send remains uncertain to avoid a duplicate; check Discord before investigating."));
        controls.Children.Add(_shared);
        controls.Children.Add(ActionButton("Save presentation preferences", () => Run(SaveControlsAsync)));
        AddTab(tabs, "Presentation", controls);
        var health = Panel(); health.Children.Add(Text("CONNECTION HEALTH", 22)); health.Children.Add(_health);
        health.Children.Add(ActionButton("Open connections", _connections)); health.Children.Add(ActionButton("Updates and release notes", _updates)); health.Children.Add(ActionButton("Copy support summary", _support));
        AddTab(tabs, "Health & updates", health);
        var setup = Panel(); setup.Children.Add(Text("READY FOR YOUR NEXT UNLOCK", 22));
        setup.Children.Add(Text("1. Connect Discord, then Xbox if you use it. Steam uses the local client.\n\n2. Run the connection checks in Connections.\n\n3. Test your local overlay and chime below.\n\n4. Start monitoring before playing. Your first snapshot quietly records existing achievements; it does not repost your history."));
        setup.Children.Add(ActionButton("Connect and verify accounts", _connections)); setup.Children.Add(ActionButton("Show sample unlock", ReplayControls));
        AddTab(tabs, "Getting started", setup);
        LoadControls();
        _search.TextChanged += (_, _) => RefreshGallery(); _platform.SelectionChanged += (_, _) => RefreshGallery(); _period.SelectionChanged += (_, _) => RefreshGallery();
        _gallery.SelectionChanged += (_, _) => SelectAchievement(); _sessions.SelectionChanged += (_, _) => RefreshRecap();
        _position.SizeChanged += (_, _) => PositionStrip();
        _position.MouseLeftButtonDown += (_, e) => { _position.CaptureMouse(); DragPosition(e); };
        _position.MouseMove += (_, e) => { if (_position.IsMouseCaptured) DragPosition(e); };
        _position.MouseLeftButtonUp += (_, _) => _position.ReleaseMouseCapture();
        _services.CompanionJournal.Changed += JournalChanged;
        _timer.Tick += (_, _) => { if (IsVisible) RefreshHealth(); }; _timer.Start();
        Closed += (_, _) => { _closed = true; _timer.Stop(); _artworkCancellation?.Cancel(); _services.CompanionJournal.Changed -= JournalChanged; };
        RefreshGallery(); RefreshSessions(); RefreshHealth();
    }

    private static TextBlock Text(string text, double size = 13) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 7, 0, 7) };
    private static StackPanel Panel() => new() { Margin = new Thickness(12) };
    private static Button ActionButton(string title, Action action)
    { var button = new Button { Content = title, Margin = new Thickness(0, 8, 10, 8), HorizontalAlignment = HorizontalAlignment.Left }; button.Click += (_, _) => action(); return button; }
    private static void AddTab(TabControl tabs, string title, StackPanel content) => tabs.Items.Add(new TabItem { Header = title, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
    private sealed record Row(JournalEntry Entry) { public string Label => $"{Entry.Achievement.GameName} · {Entry.Achievement.Name}  |  {Entry.Delivery}  |  {Entry.ObservedAt.ToLocalTime():g}"; }
    private sealed record SessionRow(string Id, DateTimeOffset Start) { public string Label => Start.ToLocalTime().ToString("f"); }
    private sealed record MonitorRow(string Id, string Label);
    private Row? Selected => _gallery.SelectedItem as Row;
    private void JournalChanged()
    { if (_closed || Dispatcher.HasShutdownStarted) return; _ = Dispatcher.InvokeAsync(() => { if (!_closed) { RefreshGallery(); RefreshSessions(); RefreshHealth(); } }); }
    private void RefreshGallery()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var id = Selected?.Entry.Achievement.Id;
            var today = DateTimeOffset.Now.Date;
            var query = _services.CompanionJournal.Snapshot.AsEnumerable();
            if (_platform.SelectedIndex > 0) query = query.Where(x => x.Achievement.SourceProvider.Contains((string)_platform.SelectedItem, StringComparison.OrdinalIgnoreCase));
            var cutoff = _period.SelectedIndex switch { 1 => today, 2 => today.AddDays(-7), 3 => today.AddDays(-30), _ => DateTime.MinValue };
            query = query.Where(x => x.ObservedAt.LocalDateTime >= cutoff);
            if (_period.SelectedIndex == 4) query = query.Where(x => x.Achievement.IsRare);
            if (_period.SelectedIndex == 5) query = query.Where(x => !x.Delivery.StartsWith("Delivered", StringComparison.Ordinal) && x.Delivery != "Filtered");
            var search = _search.Text.Trim();
            var rows = query.Where(x => string.Concat(x.Achievement.GameName, " ", x.Achievement.Name).Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.ObservedAt).Select(x => new Row(x)).ToArray();
            _gallery.ItemsSource = rows;
            _gallery.SelectedItem = rows.FirstOrDefault(x => x.Entry.Achievement.Id == id) ?? rows.FirstOrDefault();
        }
        finally { _refreshing = false; }
        SelectAchievement();
    }
    private async void SelectAchievement()
    {
        if (_refreshing) return;
        _artworkCancellation?.Cancel(); _artworkCancellation?.Dispose(); _artworkCancellation = new();
        var token = _artworkCancellation.Token;
        if (Selected is not { } row) { _details.Text = "No matching live unlocks recorded yet."; _artwork.Source = null; return; }
        var a = row.Entry.Achievement;
        _details.Text = $"{a.Name}\n{a.GameName} · {a.Platform ?? a.SourceProvider}\n{a.Description}\n{RelayRarityClassifier.FormatPercentage(a.RarityPercentage)} · {row.Entry.Delivery}";
        _artwork.Source = MainWindow.DecodeRedlineImage(a.ImageBytes, 600);
        var rule = (_settings.Companion.Games ?? []).FirstOrDefault(x => x.GameKey == CompanionPolicy.GameKey(a));
        _muteGame.IsChecked = rule?.MuteSound == true; _hideGame.IsChecked = rule?.HideOverlay == true; _rareGame.IsChecked = rule?.RareCelebrationsOnly == true;
        try
        {
            var art = await _services.ArtworkClient.GetAsync(a, token);
            if (!token.IsCancellationRequested && !_closed) _artwork.Source = MainWindow.DecodeRedlineImage(art.HeroImageBytes ?? art.AchievementIconBytes, 900);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Net.Http.HttpRequestException) { }
    }
    private void Replay() => Run(async () =>
    {
        if (Selected is not { } row) return;
        var settings = await _services.SettingsStore.LoadAsync();
        _services.AchievementOverlayService.Preview(row.Entry.Achievement, settings: CompanionPolicy.ForGame(row.Entry.Achievement, settings));
        _notice.Text = "Local replay queued. No Discord post was sent.";
    });
    private void ReplayControls() => Run(async () =>
    {
        var settings = await _services.SettingsStore.LoadAsync();
        var sample = Selected?.Entry.Achievement ?? new AchievementEvent { Id = "local-companion-preview", Name = "A signal worth celebrating", GameName = "Achievement Relay", SourceProvider = "Preview", RarityPercentage = .4, IsRare = true, RarityKnown = true };
        _services.AchievementOverlayService.Preview(sample, settings: settings with { Companion = ReadControls() });
        _notice.Text = "Testing unsaved presentation controls locally.";
    });
    private async Task RetryAsync()
    {
        if (Selected is not { } row || row.Entry.Delivery.StartsWith("Delivered", StringComparison.Ordinal) || row.Entry.Delivery == "Filtered") return;
        var settings = await _services.SettingsStore.LoadAsync();
        var result = await _services.AchievementDeliveryService.DeliverAsync(row.Entry.Achievement, settings);
        _notice.Text = result == AchievementDeliveryResult.Posted ? "Discord accepted the post." : "Delivery checks completed. See the current delivery status.";
    }
    private async Task PreviewCardAsync()
    {
        if (Selected is not { } row) return;
        var post = await _services.AchievementPostComposer.ComposeAsync(row.Entry.Achievement, _settings with { Companion = ReadControls() });
        _artwork.Source = MainWindow.DecodeRedlineImage(post.AttachmentBytes, 1200);
        _notice.Text = post.UsesCollectorCard ? "Local preview of your Discord showcase. Nothing sent." : "Compact card selected: achievement name, description, platform, rarity and available icon. Nothing sent.";
    }
    private async Task ConfirmUncertainAsync()
    {
        if (Selected is not { } row || !row.Entry.Delivery.StartsWith("Delivery uncertain", StringComparison.Ordinal)) return;
        if (System.Windows.MessageBox.Show(this,
            "Only continue if you have checked Discord and this achievement post is already present. This records your confirmation and prevents a retry; it does not send a post.",
            "Confirm existing Discord post", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var settings = await _services.SettingsStore.LoadAsync();
        var secret = _services.WebhookProtector.TryUnprotect(settings.ProtectedWebhookUrl);
        if (!WebhookUrlValidator.TryNormalize(secret, out var uri, out _) || uri is null) return;
        using var claim = SharedDeliveryClaim.Acquire(settings.Companion.SharedDeliveryFolder, row.Entry.Achievement.Id, uri);
        if (claim.State != "sending") { _notice.Text = "The shared claim has changed. Refresh delivery status before confirming."; return; }
        claim.SetState("delivered");
        await _services.EventLedger.MarkProcessedAsync(row.Entry.Achievement.Id);
        await _services.CompanionJournal.RecordAsync(row.Entry.Achievement, "Delivered (confirmed by you)");
        _notice.Text = "Existing post confirmed. No new post was sent.";
    }
    private void RefreshSessions()
    {
        var id = (_sessions.SelectedItem as SessionRow)?.Id;
        var rows = _services.CompanionJournal.Snapshot.GroupBy(x => x.SessionId).Select(g => new SessionRow(g.Key, g.Min(x => x.ObservedAt))).OrderByDescending(x => x.Start).ToArray();
        _sessions.ItemsSource = rows; _sessions.SelectedItem = rows.FirstOrDefault(x => x.Id == id) ?? rows.FirstOrDefault(); RefreshRecap();
    }
    private JournalEntry[] SessionEntries => _sessions.SelectedItem is SessionRow row ? _services.CompanionJournal.Snapshot.Where(x => x.SessionId == row.Id).ToArray() : [];
    private void RefreshRecap()
    {
        var entries = SessionEntries;
        if (entries.Length == 0) { _recap.Text = "No recorded sessions yet."; return; }
        var rarest = entries.Where(x => x.Achievement.RarityPercentage is >= 0 and <= 100).MinBy(x => x.Achievement.RarityPercentage);
        _recap.Text = $"{entries.Length} unlocks · {entries.Select(x => CompanionPolicy.GameKey(x.Achievement)).Distinct().Count()} games · {entries.Sum(x => Math.Max(0, x.Achievement.Gamerscore ?? 0))}G\n" +
            (rarest is null ? "Rarity unavailable." : $"Rarest: {rarest.Achievement.Name} · {RelayRarityClassifier.FormatPercentage(rarest.Achievement.RarityPercentage)}") + "\n\n" +
            string.Join("\n", entries.GroupBy(x => x.Achievement.GameName).Select(g => $"{g.Key} — {g.Count()} unlocks"));
    }
    private async Task ShareRecapAsync()
    {
        if (SessionEntries.Length == 0) return;
        var settings = await _services.SettingsStore.LoadAsync();
        var secret = _services.WebhookProtector.TryUnprotect(settings.ProtectedWebhookUrl);
        if (!WebhookUrlValidator.TryNormalize(secret, out var uri, out _) || uri is null) { _notice.Text = "Connect Discord first."; return; }
        var summary = _recap.Text.Length > 1800 ? _recap.Text[..1800] : _recap.Text;
        var payload = JsonSerializer.Serialize(new { username = "Achievement Relay", content = "SESSION RECAP\n" + summary, allowed_mentions = new { parse = Array.Empty<string>() } });
        var result = await _services.WebhookClient.SendAsync(uri, payload, CancellationToken.None);
        _notice.Text = result.Success ? "Session recap shared to Discord." : result.Message;
    }
    private void LoadControls()
    {
        var p = _settings.Companion; _scale.Value = p.OverlayScale; _seconds.Value = p.OverlaySeconds; _x = p.OverlayX; _y = p.OverlayY;
        _presentation.SelectedIndex = p.DiscordPresentation == DiscordPresentation.Compact ? 1 : 0;
        _rarity.IsChecked = p.RarityCelebrations; _shared.Text = p.SharedDeliveryFolder;
        var monitors = new[] { new MonitorRow("", "Follow the active game") }.Concat(System.Windows.Forms.Screen.AllScreens.Select((x, i) => new MonitorRow(x.DeviceName, $"Screen {i + 1} · {x.Bounds.Width} × {x.Bounds.Height}"))).ToArray();
        _monitor.ItemsSource = monitors; _monitor.SelectedItem = monitors.FirstOrDefault(x => x.Id == p.OverlayMonitor) ?? monitors[0];
    }
    private CompanionPreferences ReadControls() => _settings.Companion with { OverlayScale = _scale.Value, OverlaySeconds = (int)_seconds.Value, OverlayX = _x, OverlayY = _y,
        OverlayMonitor = (_monitor.SelectedItem as MonitorRow)?.Id ?? "", RarityCelebrations = _rarity.IsChecked == true,
        DiscordPresentation = _presentation.SelectedIndex == 1 ? DiscordPresentation.Compact : DiscordPresentation.Showcase, SharedDeliveryFolder = _shared.Text.Trim() };
    private void DragPosition(System.Windows.Input.MouseEventArgs e)
    { var point = e.GetPosition(_position); _x = Math.Clamp((point.X - _strip.Width / 2) / Math.Max(1, _position.ActualWidth - _strip.Width), 0, 1); _y = Math.Clamp((point.Y - _strip.Height / 2) / Math.Max(1, _position.ActualHeight - _strip.Height), 0, 1); PositionStrip(); }
    private void PositionStrip() { Canvas.SetLeft(_strip, _x * Math.Max(0, _position.ActualWidth - _strip.Width)); Canvas.SetTop(_strip, _y * Math.Max(0, _position.ActualHeight - _strip.Height)); }
    private async Task SaveControlsAsync()
    {
        var preferences = ReadControls();
        if (!string.IsNullOrEmpty(preferences.SharedDeliveryFolder))
        {
            using var probe = SharedDeliveryClaim.Acquire(preferences.SharedDeliveryFolder, "configuration-check", new Uri("https://discord.com/"));
        }
        await SaveAsync(preferences);
    }
    private async Task SaveGameAsync()
    {
        if (Selected is not { } row) return;
        var key = CompanionPolicy.GameKey(row.Entry.Achievement);
        var rule = new GamePreferences { GameKey = key, MuteSound = _muteGame.IsChecked == true, HideOverlay = _hideGame.IsChecked == true, RareCelebrationsOnly = _rareGame.IsChecked == true };
        await SaveAsync(_settings.Companion with { Games = (_settings.Companion.Games ?? []).Where(x => x.GameKey != key).Append(rule).TakeLast(300).ToArray() });
    }
    private async Task SaveAsync(CompanionPreferences preferences)
    {
        var persisted = await _services.SettingsStore.LoadAsync();
        await _services.SettingsStore.SaveAsync(persisted with { Companion = preferences });
        _settings = _settings with { Companion = preferences }; _saved(preferences); _services.AchievementOverlayService.Clear(); _notice.Text = "Preferences saved.";
    }
    private void RefreshHealth()
    {
        var xbox = _services.RelayCoordinator; var steam = _services.SteamMonitorCoordinator; var update = _services.UpdateService.Snapshot;
        if (_lastGame is not null && steam.CurrentGameName is null && SessionEntries.Length > 0)
            _notice.Text = "Game session ended. Your recap is ready in Sessions.";
        _lastGame = steam.CurrentGameName;
        var lastDelivery = _services.CompanionJournal.Snapshot.Where(x => x.Delivery == "Delivered").MaxBy(x => x.UpdatedAt);
        _health.Text = $"XBOX · {(xbox.IsRunning ? "Monitoring" : "Stopped")}\nLast successful sync: {xbox.LastSuccessfulSync?.ToLocalTime().ToString("g") ?? "Not yet"}\n{xbox.LastSyncError}\n\n" +
            $"STEAM · {steam.Phase}\n{steam.CurrentGameName ?? "Waiting for a game"}\nLast observation: {steam.LastObservationUtc?.ToLocalTime().ToString("g") ?? "Not yet"}\n{steam.LastError}\n\n" +
            $"DISCORD · Last confirmed delivery: {lastDelivery?.UpdatedAt.ToLocalTime().ToString("g") ?? "Not yet"}\nUse Gallery → Needs attention for pending posts.\n\n" +
            $"UPDATES · {update.Stage}\nInstalled: {update.CurrentVersion} · Latest: {update.LatestVersion ?? "Not checked"}\n{update.Message}\n" +
            (update.DownloadProgress is { } progress ? $"Download: {progress:0}%\n" : "") + (_services.CompanionJournal.StorageError ?? "") +
            (string.IsNullOrWhiteSpace(update.ReleaseNotes) ? "" : "\nRELEASE NOTES\n" + update.ReleaseNotes);
    }

    internal void ExportPreviews(string directory)
    {
        var root = (DockPanel)Content;
        var tabs = root.Children.OfType<TabControl>().Single();
        foreach (var index in Enumerable.Range(0, tabs.Items.Count))
        {
            tabs.SelectedIndex = index;
            root.Measure(new System.Windows.Size(980, 780));
            root.Arrange(new Rect(0, 0, 980, 780)); root.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(980, 780, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(directory, $"companion-{index}.png")); encoder.Save(file);
        }
    }
    private async void Run(Func<Task> action)
    {
        if (_busy) return; _busy = true;
        try { await action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Net.Http.HttpRequestException or OperationCanceledException)
        { _notice.Text = "The action could not finish. Check your connection, folder access and delivery status before retrying."; }
        finally { _busy = false; }
    }
}
