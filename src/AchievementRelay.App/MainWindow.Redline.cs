using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App;

public partial class MainWindow
{
    private readonly ObservableCollection<RedlineAchievement> _redlineAchievements = [];

    private void InitializeRedline()
    {
        RedlineAchievementsList.ItemsSource = _redlineAchievements;
        RedlineOverlayToggle.IsChecked = _settings.AchievementOverlayEnabled;
        _services.AchievementDeliveryService.AchievementPosted += OnRedlineAchievementPosted;
    }

    private void ShowPresentation_Click(object sender, RoutedEventArgs e) => NavigateTo(5);

    private async void RedlineOverlayToggle_Click(object sender, RoutedEventArgs e)
    {
        var enabled = RedlineOverlayToggle.IsChecked == true;
        MainTabs.IsEnabled = false;
        try
        {
            // Change only this preference; never implicitly save other fields being edited.
            var persisted = await _services.SettingsStore.LoadAsync();
            await _services.SettingsStore.SaveAsync(persisted with { AchievementOverlayEnabled = enabled });
            _settings = _settings with { AchievementOverlayEnabled = enabled };
            SettingsAchievementOverlayEnabledCheckBox.IsChecked = enabled;
            if (!enabled) _services.AchievementOverlayService.Clear();
            RefreshStatus();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RedlineOverlayToggle.IsChecked = _settings.AchievementOverlayEnabled;
            ShowMessage("The overlay preference could not be saved. Your previous setting is unchanged.", MessageBoxImage.Warning);
        }
        finally { MainTabs.IsEnabled = true; }
    }

    private void OnRedlineAchievementPosted(AchievementEvent achievement, DiscordAchievementPost post)
    {
        if (Dispatcher.HasShutdownStarted || _isExiting) return;
        // Decode small, frozen display images off the UI thread. No additional network work.
        var icon = DecodeRedlineImage(post.AchievementIconBytes, 80);
        var artwork = DecodeRedlineImage(post.HeroArtworkBytes, 900) ?? icon;
        var card = post.UsesCollectorCard ? DecodeRedlineImage(post.AttachmentBytes, 1200) : null;
        var presentation = AchievementOverlayPresentation.Create(achievement);
        var item = new RedlineAchievement(presentation.AchievementName,
            achievement.GameName ?? achievement.SourceProvider,
            achievement.Description ?? "No description supplied by the provider.",
            presentation.Platform, presentation.Percentage + " · " + presentation.TierName,
            achievement.Gamerscore is > 0 ? $"{achievement.Gamerscore} Gamerscore" : "",
            DateTimeOffset.Now.ToString("HH:mm"), icon, artwork, card);
        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_isExiting) return;
            _redlineAchievements.Insert(0, item);
            while (_redlineAchievements.Count > 8) _redlineAchievements.RemoveAt(8);
            RedlineEmptyText.Visibility = Visibility.Collapsed;
            RedlineAchievementsList.SelectedItem = item;
        });
    }

    internal static BitmapSource? DecodeRedlineImage(byte[]? bytes, int width)
    {
        if (bytes is not { Length: > 0 and <= 20_000_000 }) return null;
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = width;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { return null; }
    }

    private void RedlineAchievementSelected(object sender, SelectionChangedEventArgs e)
    {
        if (RedlineAchievementsList.SelectedItem is not RedlineAchievement item) return;
        RedlineHeroImage.Source = item.Artwork ?? new BitmapImage(new Uri("pack://application:,,,/AchievementRelay.App;component/Assets/RelayCommandDeck.png"));
        RedlinePreviewLabel.Text = "DELIVERED TO DISCORD";
        RedlineGameText.Text = item.Game;
        RedlineAchievementText.Text = item.Name;
        RedlineDescriptionText.Text = item.Description;
        RedlinePlatformText.Text = item.Platform;
        RedlineRewardText.Text = string.IsNullOrEmpty(item.Reward) ? item.Rarity : item.Reward + " · " + item.Rarity;
        RedlineDeliveryText.Text = $"Delivered at {item.Time}. Select another achievement below to inspect it.";
        RedlineCollectorImage.Source = item.Card;
        RedlineCollectorEmpty.Visibility = item.Card is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed record RedlineAchievement(string Name, string Game, string Description,
        string Platform, string Rarity, string Reward, string Time,
        ImageSource? Icon, ImageSource? Artwork, ImageSource? Card);
}
