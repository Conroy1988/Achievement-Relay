using System.IO;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App;

public partial class App
{
    private static async Task<int> ExportUnlockSequenceAsync(string[] args)
    {
        if (args.Length != 2) return 2;
        using var cancellation = new CancellationTokenSource();
        Task? showing = null;
        try
        {
            var directory = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(directory);
            var sample = new AchievementEvent
            {
                Id = "local-animation-preview", Name = "Against All Odds", Description = "A local animation preview.",
                GameName = "Achievement Relay", SourceProvider = "Steam", Platform = "Steam",
                RarityKnown = true, RarityPercentage = 1.2, UnlockedAt = DateTimeOffset.UtcNow
            };
            var window = new AchievementOverlayWindow(AchievementOverlayPresentation.Create(sample),
                new AppSettings { AchievementOverlaySoundEnabled = false });
            showing = window.ShowForAsync(cancellation.Token);
            window.StartPreviewEffects();
            var elapsed = 0;
            foreach (var time in new[] { 100, 300, 700, 1000, 2500, 4700 })
            {
                await Task.Delay(time - elapsed);
                File.WriteAllBytes(Path.Combine(directory, $"unlock-{time:D4}.png"), window.CapturePreview());
                elapsed = time;
            }
            await showing;
            File.WriteAllBytes(Path.Combine(directory, "relay-unlock-15-percent.wav"), UnlockChime.CreateWave(15));
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            cancellation.Cancel();
            if (showing is not null) { try { await showing; } catch (Exception) { } }
        }
    }
}
