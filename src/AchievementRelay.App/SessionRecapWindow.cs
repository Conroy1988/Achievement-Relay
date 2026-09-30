using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AchievementRelay.App.Services;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
using Orientation = System.Windows.Controls.Orientation;

namespace AchievementRelay.App;

/// <summary>Confirmation UI only. Only its caller can send the immutable preview.</summary>
public sealed class SessionRecapWindow : Window
{
    internal bool SendIsDefault { get; }
    internal string PreviewContent { get; }

    public SessionRecapWindow(SessionRecapPresentation recap)
    {
        PreviewContent = recap.Content;
        Style = (Style)FindResource(typeof(Window)); Title = "Achievement Relay · Recap preview";
        Width = 800; Height = 820; MinWidth = 620; MinHeight = 580;
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width); MinHeight = Math.Min(MinHeight, area.Height);
        Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; UseLayoutRounding = true;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/AchievementRelay.App;component/Redline.xaml", UriKind.Relative) });
        var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        header.Children.Add(new System.Windows.Shapes.Path { Data = (Geometry)FindResource("TkbBarsGeometry"), Fill = (Brush)FindResource("AccentBrush"), Width = 48, Height = 34, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 16, 0) });
        header.Children.Add(Text("YOUR SESSION. READY TO SHARE.", 23)); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(Text("Nothing has been sent. Sharing again creates another post—check Discord before retrying an unconfirmed send.", 13));
        var buttons = new WrapPanel(); footer.Children.Add(buttons);
        var cancel = new Button { Content = "Keep it local", IsCancel = true, IsDefault = true, Margin = new Thickness(0, 8, 12, 0) };
        buttons.Children.Add(cancel);
        var send = new Button { Content = "Send this recap to Discord", IsEnabled = recap.Unlocks > 0, Margin = new Thickness(0, 8, 0, 0) };
        SendIsDefault = send.IsDefault; send.Click += (_, _) => DialogResult = true; buttons.Children.Add(send);
        var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        var metrics = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 14) };
        foreach (var metric in new[] { ("UNLOCKS", recap.Unlocks.ToString()), ("GAME / PLATFORM", recap.Games.ToString()), ("GAMERSCORE", recap.Gamerscore + "G") }) {
            var content = new StackPanel(); content.Children.Add(Text(metric.Item2, 32)); content.Children.Add(Text(metric.Item1, 12));
            metrics.Children.Add(new Border { Style = (Style)FindResource("RedlineMetric"), Margin = new Thickness(0, 0, 8, 0), Child = content });
        }
        body.Children.Add(metrics); body.Children.Add(Text("DISCORD MESSAGE PREVIEW", 15));
        body.Children.Add(Text("This frozen snapshot will go to your configured Discord webhook only after you choose Send. New unlocks arriving while this window is open are not added.", 13));
        var message = Text(recap.Content, 16); System.Windows.Automation.AutomationProperties.SetName(message, "Exact recap message text");
        body.Children.Add(new Border { Style = (Style)FindResource("RedlinePanel"), Child = message });
        body.Children.Add(Text("Message text is shown above; Discord renders markdown and wrapping in its own client.", 12));
    }
    private static TextBlock Text(string text, double size) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) };
}
