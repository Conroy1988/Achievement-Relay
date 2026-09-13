using System.IO;
using System.Windows;
using CheckBox = System.Windows.Controls.CheckBox;
using System.Windows.Media;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App;

public partial class App
{
    private static async Task<int> ExportUnlockSequenceAsync(string[] args)
    {
        if (args.Length != 2) return 2;
        var temporaryData = Directory.CreateTempSubdirectory("relay-unlock-test-");
        var services = new AppServices(new AppPaths(temporaryData.FullName));
        MainWindow? main = null;
        try
        {
            var directory = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(directory);
            main = new MainWindow(services, new AppSettings
            {
                AchievementOverlayAnimationEnabled = false,
                AchievementOverlaySoundEnabled = false
            }, previewOnly: true);
            ((System.Windows.Controls.Button)main.FindName("SettingsNavButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var animate = (CheckBox)main.FindName("SettingsOverlayAnimationCheckBox");
            var reduced = (CheckBox)main.FindName("SettingsOverlayReducedMotionCheckBox");
            var followWindows = (CheckBox)main.FindName("SettingsOverlayFollowWindowsCheckBox");
            var test = (System.Windows.Controls.Button)main.FindName("TestUnlockButton");
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<AchievementOverlayWindow> ClickTestAsync()
            {
                var started = new TaskCompletionSource<AchievementOverlayWindow>(TaskCreationOptions.RunContinuationsAsynchronously);
                closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void Observe(AchievementOverlayWindow window)
                {
                    window.Closed += (_, _) => closed.TrySetResult();
                    started.TrySetResult(window);
                }
                services.AchievementOverlayService.PresentationStarted += Observe;
                try
                {
                    test.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    return await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
                }
                finally { services.AchievementOverlayService.PresentationStarted -= Observe; }
            }
            // Unsaved controls, real button handler, real queue and real ShowForAsync.
            // No effect invocation or operating-system preference override is permitted here.
            animate.IsChecked = true;
            reduced.IsChecked = false;
            followWindows.IsChecked = false;
            var window = await ClickTestAsync();
            var elapsed = 0;
            foreach (var time in new[] { 100, 300, 700, 1000, 2500, 4700 })
            {
                await Task.Delay(time - elapsed);
                File.WriteAllBytes(Path.Combine(directory, $"unlock-{time:D4}.png"), window.CapturePreview());
                elapsed = time;
            }
            var countdown = (ScaleTransform)window.FindName("CountdownScale");
            if (!SystemParameters.HighContrast && (!countdown.HasAnimatedProperties || countdown.ScaleX >= .3))
                throw new InvalidOperationException("Real Test unlock did not animate or drain its countdown.");
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            foreach (var mode in new[] { "static", "reduced" })
            {
                animate.IsChecked = mode != "static";
                reduced.IsChecked = mode == "reduced";
                window = await ClickTestAsync();
                await Task.Delay(400);
                if (((ScaleTransform)window.FindName("CountdownScale")).HasAnimatedProperties ||
                    ((ScaleTransform)window.FindName("ArtworkPulse")).HasAnimatedProperties)
                    throw new InvalidOperationException($"{mode} mode unexpectedly played decorative motion.");
                File.WriteAllBytes(Path.Combine(directory, $"mode-{mode}.png"), window.CapturePreview());
                services.AchievementOverlayService.Clear();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
            if (File.Exists(services.Paths.SettingsFile))
                throw new InvalidOperationException("Test unlock unexpectedly saved settings.");
            File.WriteAllText(Path.Combine(directory, "motion-verification.txt"),
                $"Real Settings button → Preview → queue → ShowForAsync verified.\nWindows animations: {SystemParameters.ClientAreaAnimation}\nHigh contrast: {SystemParameters.HighContrast}\nFull, static, reduced and unsaved preference behavior checked.\n");
            File.WriteAllBytes(Path.Combine(directory, "relay-unlock-15-percent.wav"), UnlockChime.CreateWave(15));
            await main.VerifySettingsFailureAsync();
            await services.CompanionJournal.RecordAsync(new AchievementEvent
            {
                Id = "native-companion-fixture", Name = "Beyond the horizon", GameName = "Relay Showcase",
                Description = "A complete achievement description with enough space to read every detail.",
                SourceProvider = "Steam", RarityKnown = true, IsRare = true, RarityPercentage = .4
            }, "Delivered");
            await services.CompanionLibrary.ObserveAsync("fixture", "Relay Showcase", "Steam", 47, 50, null,
                new[] { new AchievementEvent { Id = "historic-fixture", Name = "The first signal", GameName = "Relay Showcase", SourceProvider = "Steam", Description = "Imported history stays local.", UnlockedAt = DateTimeOffset.UtcNow.AddDays(-2) } }, true);
            var now = DateTimeOffset.UtcNow;
            await services.CompanionJournal.MergeAccountHistoryAsync(Enumerable.Range(0, 299).Select(i =>
                new JournalEntry(new AchievementEvent { Id = $"review-{i}", Name = $"Historical achievement {i} with a long readable title", GameName = "A game with a long name and no artwork", SourceProvider = "Preview", IsHistorical = true }, now.AddMinutes(-i), "imported-session", "Delivery uncertain", now)));
            await services.CompanionLibrary.MergeAccountHistoryAsync(Enumerable.Range(0, 100).Select(i =>
                new LibraryGame($"review-game-{i}", $"Fixture game {i:D3}", "Preview", i, i % 3 == 0 ? null : 100, null, now.AddSeconds(i), [])));
            var companion = new CompanionWindow(services, new AppSettings(), _ => { }, () => { }, () => { }, () => { });
            try
            {
                await companion.VerifyUsabilityAsync();
                companion.ExportPreviews(directory);
                await companion.VerifyCustomPreviewAsync();
                File.AppendAllText(Path.Combine(directory, "motion-verification.txt"), "Companion button: unsaved 135% size, bottom-right placement and 3-second duration verified.\n");
                File.WriteAllText(Path.Combine(directory, "usability-verification.txt"), "PASS: failed Settings save preserves edits and restores controls.\nPASS: 300-entry gallery, 100-game library, long titles and missing artwork.\nPASS: imported history excluded from local sessions and labelled in trophies.\nPASS: historical retry and confirmation are disabled and inert.\nPASS: per-game draft survives filtering; discard applies pending filters.\nPASS: library search, empty result clearing and name sorting.\nPASS: failed Companion save retains draft; retry persists it.\nNo real accounts, cloud records or Discord posts used.\n");
            }
            finally { companion.Close(); }
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            services.AchievementOverlayService.Clear();
            main?.PrepareForExit();
            main?.Close();
            services.Dispose();
            temporaryData.Delete(recursive: true);
        }
    }
}
