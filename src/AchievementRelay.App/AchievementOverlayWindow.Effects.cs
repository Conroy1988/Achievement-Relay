using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App;

public partial class AchievementOverlayWindow
{
    private void StartUnlockEffects()
    {
        UnlockSweep.Opacity = 1;
        SweepPosition.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-110, 540, TimeSpan.FromMilliseconds(650))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var pulse = new DoubleAnimationUsingKeyFrames();
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(.82, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(260)), new CubicEase { EasingMode = EasingMode.EaseOut }));
        pulse.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(470))));
        ArtworkPulse.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
        ArtworkPulse.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
        RarityShimmer.Opacity = 1;
        ShimmerPosition.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-28, 150, TimeSpan.FromMilliseconds(700)) { BeginTime = TimeSpan.FromMilliseconds(500) });
        AchievementNameText.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = TimeSpan.FromMilliseconds(120) });
        if (_presentation.Tier == RelayRarityTier.Platinum)
            PlatinumSparkle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, .8, TimeSpan.FromMilliseconds(350)) { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(650) });
        CountdownLine.Opacity = 1;
        CountdownScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, DisplayDuration) { BeginTime = TimeSpan.FromMilliseconds(240) });
    }

    private void StopUnlockEffects()
    {
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
}
