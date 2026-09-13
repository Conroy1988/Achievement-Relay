using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AchievementRelay.Core.Models;
using Color = System.Windows.Media.Color;

namespace AchievementRelay.App;

public partial class AchievementOverlayWindow
{
    private void StartUnlockEffects()
    {
        // Expand a two-pixel beam before revealing the artwork and text.
        var clip = new RectangleGeometry(new Rect(258, 34, 4, 2));
        OverlayFrame.Clip = clip;
        var reveal = new RectAnimationUsingKeyFrames();
        reveal.KeyFrames.Add(new LinearRectKeyFrame(new Rect(0, 34, 516, 2), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(180))));
        reveal.KeyFrames.Add(new LinearRectKeyFrame(new Rect(0, 0, 516, 72), KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(430))));
        clip.BeginAnimation(RectangleGeometry.RectProperty, reveal);
        UnlockSweep.Opacity = 1;
        SweepPosition.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-110, 540, TimeSpan.FromMilliseconds(650))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var pulse = new DoubleAnimationUsingKeyFrames();
        var celebratory = _preferences.Companion.RarityCelebrations &&
            (_presentation.Tier is RelayRarityTier.Gold or RelayRarityTier.Platinum || _presentation.Eyebrow.StartsWith("100%", StringComparison.Ordinal));
        if (celebratory)
        {
            var completion = _presentation.Eyebrow.StartsWith("100%", StringComparison.Ordinal);
            var count = completion ? 24 : _presentation.Tier == RelayRarityTier.Platinum ? 18 : 10;
            for (var i = 0; i < count; i++)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = i % 3 + 2, Height = i % 3 + 2,
                    Fill = new SolidColorBrush(_presentation.Tier == RelayRarityTier.Gold ? Color.FromRgb(255, 206, 99) : Color.FromRgb(210, 240, 255)), Opacity = 0 };
                var move = new TranslateTransform(260, 38); dot.RenderTransform = move; CelebrationParticles.Children.Add(dot);
                var delay = TimeSpan.FromMilliseconds(430 + i * 18);
                var angle = i * Math.PI * 2 / count;
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(260, 260 + Math.Cos(angle) * 245, TimeSpan.FromMilliseconds(850)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(38, 38 + Math.Sin(angle) * 33, TimeSpan.FromMilliseconds(850)) { BeginTime = delay });
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(1000)) { BeginTime = delay });
            }
        }
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(.82, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(celebratory ? 1.16 : 1.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(470))));
        ArtworkPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        ArtworkPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        RarityShimmer.Opacity = 1;
        ShimmerPosition.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-28, 150, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(500) });
        AchievementNameText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        if (_preferences.Companion.RarityCelebrations && _presentation.Tier == RelayRarityTier.Platinum)
            PlatinumSparkle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, .8, TimeSpan.FromMilliseconds(350)) { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(650) });
        CountdownLine.Opacity = 1;
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, HoldDuration) { BeginTime = TimeSpan.FromMilliseconds(240) });
    }

    private void StopUnlockEffects()
    {
        if (OverlayFrame.Clip is RectangleGeometry clip) clip.BeginAnimation(RectangleGeometry.RectProperty, null);
        OverlayFrame.Clip = null;
        CelebrationParticles.Children.Clear();
        UnlockSweep.Opacity = 0;
        RarityShimmer.Opacity = 0;
        CountdownLine.Opacity = 0;
        SweepPosition.BeginAnimation(TranslateTransform.XProperty, null);
        ShimmerPosition.BeginAnimation(TranslateTransform.XProperty, null);
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ArtworkPulse.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ArtworkPulse.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        AchievementNameText.BeginAnimation(OpacityProperty, null);
        PlatinumSparkle.BeginAnimation(OpacityProperty, null);
        AchievementNameText.Opacity = 1;
        PlatinumSparkle.Opacity = 0;
    }

    private void RetractUnlockEffects()
    {
        var clip = new RectangleGeometry(new Rect(0, 0, 516, 72)); OverlayFrame.Clip = clip;
        clip.BeginAnimation(RectangleGeometry.RectProperty, new RectAnimation(new Rect(0, 0, 516, 72), new Rect(240, 34, 36, 2), TimeSpan.FromMilliseconds(180)));
    }
}
