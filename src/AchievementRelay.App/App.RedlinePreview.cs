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
            Render(window, Path.Combine(Path.GetDirectoryName(output)!,
                Path.GetFileNameWithoutExtension(output) + "_compact.png"), 1100, 640);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
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

    private static void Render(MainWindow window, string output, int width, int height)
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
