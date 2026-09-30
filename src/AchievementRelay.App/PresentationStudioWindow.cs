using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using Image = System.Windows.Controls.Image;
using Orientation = System.Windows.Controls.Orientation;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Brush = System.Windows.Media.Brush;

namespace AchievementRelay.App;

/// <summary>Presentation-only: no settings store, delivery service, journal or webhook client.</summary>
public sealed class PresentationStudioWindow : Window
{
    private readonly DiscordAchievementPostComposer _composer;
    private readonly AppSettings _settings;
    private readonly Action<AchievementEvent> _preview;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ComboBox _source = new() { DisplayMemberPath = "Label" };
    private readonly ComboBox _format = new() { ItemsSource = new[] { "Artwork showcase", "Compact embed" } };
    private readonly ComboBox _size = new() { ItemsSource = new[] { "Desktop · 560", "Small · 360", "Full artwork · 1200" }, SelectedIndex = 0 };
    private readonly Image _card = new() { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Image _strip = new() { Stretch = Stretch.Uniform, MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly StackPanel _embed = new() { MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _status = Text("Choose a source, then render a preview. Nothing is posted or saved.");
    private readonly TextBlock _sourceLabel = Text("");
    private readonly Button _render = new() { Content = "Render preview", Margin = new Thickness(0, 8, 10, 8) };
    private bool _closed;
    private bool _busy;
    private sealed record SourceRow(string Label, AchievementEvent Achievement)
    {
        // The shared ComboBox template displays SelectionBoxItem directly.
        public override string ToString() => Label;
    }

    public PresentationStudioWindow(DiscordAchievementPostComposer composer, AppSettings settings,
        AchievementEvent? selected, Action<AchievementEvent> preview)
    {
        _composer = composer; _settings = settings; _preview = preview;
        Style = (Style)FindResource(typeof(Window));
        Title = "Achievement Relay · Presentation Studio"; Width = 960; Height = 880; MinWidth = 640; MinHeight = 580;
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width); MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AchievementRelay.App;component/CompanionStyles.xaml", UriKind.Relative) });
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AchievementRelay.App;component/Redline.xaml", UriKind.Relative) });
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new System.Windows.Shapes.Path { Data = (Geometry)FindResource("TkbBarsGeometry"), Fill = (Brush)FindResource("AccentBrush"), Width = 48, Height = 34, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 18, 0) });
        heading.Children.Add(Text("PRESENTATION STUDIO", 25)); DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        body.Children.Add(Text("LOCAL PREVIEW ONLY · TKB REDLINE", 13));
        body.Children.Add(Text("Compare the real card renderer at different sizes. Studio choices are temporary; saved settings and Discord delivery stay unchanged."));
        body.Children.Add(Text("Achievement source")); body.Children.Add(_source);
        var options = new Grid(); options.ColumnDefinitions.Add(new ColumnDefinition()); options.ColumnDefinitions.Add(new ColumnDefinition());
        var formats = new StackPanel { Margin = new Thickness(0, 0, 10, 0) }; formats.Children.Add(Text("Card format")); formats.Children.Add(_format);
        var sizes = new StackPanel(); sizes.Children.Add(Text("Preview width (logical pixels)")); sizes.Children.Add(_size);
        Grid.SetColumn(sizes, 1); options.Children.Add(formats); options.Children.Add(sizes); body.Children.Add(options);
        var actions = new WrapPanel(); actions.Children.Add(_render);
        var replay = new Button { Content = "Test animated strip locally", Margin = new Thickness(0, 8, 10, 8) };
        replay.Click += (_, _) => { if (_source.SelectedItem is SourceRow row) { _preview(row.Achievement); _status.Text = "Local preview requested. Master sound, reduced-motion and quiet settings still apply. Nothing posted."; } };
        actions.Children.Add(replay); body.Children.Add(actions);
        body.Children.Add(_sourceLabel);
        body.Children.Add(new Border { Padding = new Thickness(14), Background = (Brush)FindResource("PanelRaisedBrush"), CornerRadius = new CornerRadius(8), Child = new StackPanel { Children = { _card, _embed } } });
        body.Children.Add(Text("SIGNAL STRIP · STATIC REFERENCE", 16)); body.Children.Add(_strip);
        body.Children.Add(Text("Use Test animated strip locally to see the real reveal and hear the chime. Position, monitor, scale and duration come from your current Companion draft. Change them in Presentation, then reopen the studio."));
        body.Children.Add(Text($"Motion: {(settings.AchievementOverlayReducedMotion ? "reduced" : settings.AchievementOverlayAnimationEnabled ? "enabled" : "off")} · Master sound: {(settings.AchievementOverlaySoundEnabled ? "on" : "off")}. Windows accessibility preferences can further reduce motion."));
        var rows = CreateSamples().Select(x => new SourceRow($"SAMPLE · {x.SourceProvider} · {x.Name}", x)).ToList();
        if (selected is not null) rows.Insert(0, new SourceRow("SELECTED · " + selected.Name, selected));
        _source.ItemsSource = rows; _source.SelectedIndex = 0;
        _format.SelectedIndex = settings.Companion.DiscordPresentation == DiscordPresentation.Compact ? 1 : 0;
        _render.Click += async (_, _) => await RenderAsync();
        _source.SelectionChanged += (_, _) => InvalidatePreview();
        _format.SelectionChanged += (_, _) => InvalidatePreview();
        _size.SelectionChanged += (_, _) => SetPreviewSize();
        System.Windows.Automation.AutomationProperties.SetName(_source, "Studio achievement source");
        System.Windows.Automation.AutomationProperties.SetName(_format, "Temporary preview card format");
        System.Windows.Automation.AutomationProperties.SetName(_size, "Card preview width in logical pixels");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
        Loaded += async (_, _) => await RenderAsync();
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _lifetime.Dispose(); };
        SetPreviewSize();
    }

    internal static AchievementEvent[] CreateSamples() => new[] { "Steam", "Xbox" }.SelectMany(provider =>
        new[] { ("Beyond the horizon", 55d, false), ("Against all odds", 3.8d, false), ("The final chapter", .2d, true) }.Select(sample =>
        new AchievementEvent { Id = $"studio-sample-{provider}-{sample.Item1}", Name = sample.Item1, GameName = "Redline sample adventure", SourceProvider = provider,
            Platform = provider == "Xbox" ? "Xbox PC" : "Steam", PlayerName = "Sample player", Gamerscore = provider == "Xbox" ? 50 : null,
            Description = "A synthetic achievement used only to preview your presentation.", RarityKnown = true, RarityPercentage = sample.Item2,
            IsRare = sample.Item2 < 10, IsGameCompletion = sample.Item3, VerifiedAchievementTotal = sample.Item3 ? 40 : null })).ToArray();

    private static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
    private void SetPreviewSize() { _card.MaxWidth = _embed.MaxWidth = _size.SelectedIndex switch { 1 => 360, 2 => 1200, _ => 560 }; }
    private void InvalidatePreview()
    {
        _card.Source = null; _embed.Children.Clear(); _strip.Source = null; _sourceLabel.Text = "";
        _status.Text = "Source or format changed. Render a fresh preview; saved settings are unchanged.";
    }
    private async Task RenderAsync()
    {
        if (_closed || _busy || _source.SelectedItem is not SourceRow row) return;
        _busy = true; _render.IsEnabled = _source.IsEnabled = _format.IsEnabled = false;
        var token = _lifetime.Token;
        try
        {
            _status.Text = "Rendering locally…";
            var settings = _settings with { Companion = _settings.Companion with { DiscordPresentation = _format.SelectedIndex == 1 ? DiscordPresentation.Compact : DiscordPresentation.Showcase } };
            var post = await _composer.ComposeAsync(row.Achievement, settings, token);
            if (_closed || token.IsCancellationRequested) return;
            _card.Source = post.UsesCollectorCard ? MainWindow.DecodeRedlineImage(post.AttachmentBytes, 1200) : null;
            _embed.Children.Clear();
            if (!post.UsesCollectorCard) RenderEmbedText(post.JsonPayload);
            _strip.Source = MainWindow.DecodeRedlineImage(AchievementOverlayWindow.RenderPreview(AchievementOverlayPresentation.Create(row.Achievement, post.AchievementIconBytes)), 520);
            _sourceLabel.Text = row.Label + (post.UsesCollectorCard ? " · Actual rendered artwork" : " · Text from actual embed payload; approximate layout");
            System.Windows.Automation.AutomationProperties.SetName(_card, row.Achievement.Name + " card preview");
            System.Windows.Automation.AutomationProperties.SetName(_strip, AchievementOverlayPresentation.Create(row.Achievement).AccessibleAnnouncement);
            _status.Text = "Preview ready. Nothing sent, queued, imported or saved. Discord may scale the image differently.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or JsonException)
        { if (!_closed) { InvalidatePreview(); _status.Text = "Preview unavailable. Monitoring and delivery are unaffected."; } }
        finally { _busy = false; if (!_closed) _render.IsEnabled = _source.IsEnabled = _format.IsEnabled = true; }
    }
    private void RenderEmbedText(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        var embed = json.RootElement.GetProperty("embeds")[0];
        if (embed.TryGetProperty("title", out var title)) _embed.Children.Add(Text(title.GetString() ?? "", 20));
        if (embed.TryGetProperty("description", out var description)) _embed.Children.Add(Text(description.GetString() ?? ""));
        if (embed.TryGetProperty("fields", out var fields)) foreach (var field in fields.EnumerateArray())
            _embed.Children.Add(Text($"{field.GetProperty("name").GetString()}\n{field.GetProperty("value").GetString()}"));
        _embed.Children.Add(Text("Text/layout reference only; Discord renders markdown, thumbnails and timestamps in its own client.", 12));
    }

    internal async Task PrepareReviewAsync(int source, bool compact, bool small)
    {
        _source.SelectedIndex = source; _format.SelectedIndex = compact ? 1 : 0; _size.SelectedIndex = small ? 1 : 0;
        await RenderAsync();
        if (_source.SelectedItem is not SourceRow row || row.ToString() != row.Label)
            throw new InvalidOperationException("Studio source picker exposed an internal record instead of its label.");
        if (_strip.Source is null || (compact ? _embed.Children.Count == 0 : _card.Source is null))
            throw new InvalidOperationException("Studio fixture failed to render the expected card and strip.");
        if (_settings.Companion.DiscordPresentation != DiscordPresentation.Showcase)
            throw new InvalidOperationException("Studio preview changed the source settings.");
    }
}
