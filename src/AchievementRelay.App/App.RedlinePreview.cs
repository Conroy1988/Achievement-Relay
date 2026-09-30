using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AchievementRelay.App.Services;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App;

public partial class App
{
    // Native WPF screenshots, with isolated storage and no monitoring, tray, or network startup.
    private static bool TryExportRedlinePreview(string[] args, out int exitCode)
    {
        exitCode = 0;
        if (args.Length == 0 || args[0] != "--export-redline-preview") return false;
        if (args.Length != 2) { exitCode = 2; return true; }
        var temporaryData = Directory.CreateTempSubdirectory("relay-redline-preview-");
        MainWindow? window = null;
        try
        {
            using var services = new AppServices(new AppPaths(temporaryData.FullName));
            window = new MainWindow(services, new AppSettings(), previewOnly: true);
            var output = Path.GetFullPath(args[1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            Render(window, output, 1440, 920);
            window.VerifyDraftProtection();
            for (var page = 0; page < 6; page++) {
                window.SelectReviewPage(page);
                Render(window, Path.Combine(Path.GetDirectoryName(output)!, $"page-{page}.png"), 1100, 760);
            }
            var account = new AccountWindow(services, () => Task.CompletedTask);
            Render(account, Path.Combine(Path.GetDirectoryName(output)!, "account.png"), 620, 780);
            account.Close();
            var companion = new CompanionWindow(services, new AppSettings(), _ => {}, () => {}, () => {}, () => {});
            companion.ExportPreviews(Path.GetDirectoryName(output)!);
            companion.Close();
            window.SelectReviewPage(0);
            Render(window, Path.Combine(Path.GetDirectoryName(output)!, "home-small.png"), 900, 600);
            Render(window, Path.Combine(Path.GetDirectoryName(output)!,
                Path.GetFileNameWithoutExtension(output) + "_compact.png"), 1100, 640);
            // Synthetic populated state exercises the real dashboard projection without provider/network access.
            Task.Run(async () =>
            {
                await services.CompanionLibrary.ObserveAsync("Steam:preview:123", "Redline preview game", "Steam", 18, 40, null, [], false);
                await services.CompanionLibrary.ObserveAsync("Xbox:preview:456", "A completed Xbox adventure", "Xbox", 32, 32, null, [], false);
                await services.CompanionLibrary.ObserveAsync("Steam:preview:789", "A game with an unknown total", "Steam", 4, null, null, [], false);
                await services.CompanionLibrary.Activity.RecordAsync("123", "Redline preview game", "Steam", DateTimeOffset.UtcNow.AddMinutes(-5));
                await services.CompanionJournal.RecordAsync(new AchievementEvent { Id = "preview-delivered", Name = "Beyond the horizon",
                    GameName = "Redline preview game", SourceProvider = "Steam", RarityKnown = true, RarityPercentage = 3.8 }, "Delivered");
                await services.CompanionJournal.RecordAsync(new AchievementEvent { Id = "preview-waiting", Name = "One more step",
                    GameName = "Redline preview game", SourceProvider = "Steam" }, "Retry pending");
            }).GetAwaiter().GetResult();
            window.PrepareForExit();
            window.Close();
            window = new MainWindow(services, new AppSettings(), previewOnly: true);
            Render(window, Path.Combine(Path.GetDirectoryName(output)!, "home-populated.png"), 1440, 920);
            Render(window, Path.Combine(Path.GetDirectoryName(output)!, "home-populated-compact.png"), 900, 640);
            var collection = new CompanionWindow(services, new AppSettings(), _ => {}, () => {}, () => {}, () => {});
            collection.SelectSection("Library");
            Render(collection, Path.Combine(Path.GetDirectoryName(output)!, "companion-library-populated.png"), 1020, 820);
            Render(collection, Path.Combine(Path.GetDirectoryName(output)!, "companion-library-compact.png"), 760, 620);
            collection.SelectSection("Sessions");
            Render(collection, Path.Combine(Path.GetDirectoryName(output)!, "companion-session-populated.png"), 1020, 820);
            collection.Close();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            // WinExe runners may not attach stderr. Preserve fixture-only diagnostics for CI.
            try { File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!, "redline-preview-error.txt"), exception.ToString()); }
            catch (IOException) { }
            exitCode = 1;
        }
        finally
        {
            window?.PrepareForExit();
            window?.Close();
            temporaryData.Delete(recursive: true);
        }
        return true;
    }

    private static void Render(Window window, string output, int width, int height)
    {
        window.Width = width;
        window.Height = height;
        var content = (FrameworkElement)window.Content;
        content.Measure(new System.Windows.Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(output);
        encoder.Save(stream);
    }
}
