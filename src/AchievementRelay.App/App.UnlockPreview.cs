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
            await services.CompanionJournal.RecordAsync(new AchievementEvent
            {
                Id = "native-companion-fixture", Name = "Beyond the horizon", GameName = "Relay Showcase",
                Description = "A complete achievement description with enough space to read every detail.",
                SourceProvider = "Steam", RarityKnown = true, IsRare = true, RarityPercentage = .4
            }, "Delivered");
            var companion = new CompanionWindow(services, new AppSettings(), _ => { }, () => { }, () => { }, () => { });
            try
            {
                companion.ExportPreviews(directory);
                await companion.VerifyCustomPreviewAsync();
                File.AppendAllText(Path.Combine(directory, "motion-verification.txt"), "Companion button: unsaved 135% size, bottom-right placement and 3-second duration verified.\n");
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
