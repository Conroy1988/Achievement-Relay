using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AchievementRelay.App.Services;

public sealed record SteamGameInfo(uint AppId, string Name, string InstallDirectory);

public sealed class SteamGameDetector
{
    private static readonly TimeSpan CatalogRefreshInterval = TimeSpan.FromMinutes(1);
    private static readonly Regex PathEntry = new(
        "\\\"path\\\"\\s+\\\"(?<value>[^\\\"]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex LegacyLibraryEntry = new(
        "\\\"\\d+\\\"\\s+\\\"(?<value>[A-Za-z]:\\\\[^\\\"]+)\\\"",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ManifestName = new(
        "\\\"name\\\"\\s+\\\"(?<value>[^\\\"]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ManifestInstallDirectory = new(
        "\\\"installdir\\\"\\s+\\\"(?<value>[^\\\"]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly object _catalogGate = new();
    private IReadOnlyList<SteamGameInfo> _installedGames = Array.Empty<SteamGameInfo>();
    private DateTimeOffset _catalogExpiresUtc;

    public bool IsSteamInstalled => TryGetSteamDirectory(out _);

    public bool IsSteamRunning
    {
        get
        {
            try
            {
                var processes = Process.GetProcessesByName("steam");
                var running = false;
                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            running = true;
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                return running;
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                return false;
            }
        }
    }

    public SteamGameInfo? Detect(SteamGameInfo? currentGame)
    {
        var runningAppId = ReadRunningAppId();
        if (runningAppId > 0 && TryResolveGame(runningAppId, out var detected))
        {
            return detected;
        }

        if (currentGame is null && !IsSteamRunning)
        {
            return null;
        }

        var processPaths = GetRunningExecutablePaths();
        if (currentGame is not null && ProcessBelongsToGame(processPaths, currentGame))
        {
            return currentGame;
        }

        return GetInstalledGames()
            .Where(game => ProcessBelongsToGame(processPaths, game))
            .OrderByDescending(game => game.InstallDirectory.Length)
            .FirstOrDefault();
    }

    public uint ReadRunningAppId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess", writable: false);
            var value = key?.GetValue("RunningAppID");
            return value switch
            {
                int number when number > 0 => (uint)number,
                uint number when number > 0 => number,
                long number when number is > 0 and <= uint.MaxValue => (uint)number,
                string text when uint.TryParse(text, out var number) => number,
                _ => 0
            };
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return 0;
        }
    }

    private static IReadOnlyCollection<string> GetRunningExecutablePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                var path = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(Path.GetFullPath(path));
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Protected system and anti-cheat processes are expected here.
            }
            finally
            {
                process.Dispose();
            }
        }

        return paths;
    }

    private static bool ProcessBelongsToGame(IReadOnlyCollection<string> processPaths, SteamGameInfo game)
    {
        if (string.IsNullOrWhiteSpace(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory))
        {
            return false;
        }

        var root = Path.GetFullPath(game.InstallDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return processPaths.Any(path => path.StartsWith(root, StringComparison.OrdinalIgnoreCase));
    }

    private bool TryResolveGame(uint appId, out SteamGameInfo? game)
    {
        game = GetInstalledGames().FirstOrDefault(item => item.AppId == appId);
        if (game is not null)
        {
            return true;
        }

        game = null;
        foreach (var library in GetSteamLibraries())
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
            if (TryReadManifest(library, manifest, out game))
            {
                return true;
            }
        }

        return false;
    }

    private IReadOnlyList<SteamGameInfo> GetInstalledGames()
    {
        lock (_catalogGate)
        {
            if (DateTimeOffset.UtcNow < _catalogExpiresUtc)
            {
                return _installedGames;
            }

            var games = new Dictionary<uint, SteamGameInfo>();
            foreach (var library in GetSteamLibraries())
            {
                var steamApps = Path.Combine(library, "steamapps");
                try
                {
                    foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
                    {
                        if (TryReadManifest(library, manifest, out var game) && game is not null)
                        {
                            games[game.AppId] = game;
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    // A library can be unavailable while another one remains usable.
                }
            }

            _installedGames = games.Values.ToArray();
            _catalogExpiresUtc = DateTimeOffset.UtcNow + CatalogRefreshInterval;
            return _installedGames;
        }
    }

    private static bool TryReadManifest(string library, string manifest, out SteamGameInfo? game)
    {
        game = null;
        if (!File.Exists(manifest))
        {
            return false;
        }

        try
        {
            var fileName = Path.GetFileNameWithoutExtension(manifest);
            var separator = fileName.LastIndexOf('_');
            if (separator < 0 || !uint.TryParse(fileName[(separator + 1)..], out var appId) || appId == 0)
            {
                return false;
            }

            var text = File.ReadAllText(manifest);
            var name = Unescape(ManifestName.Match(text).Groups["value"].Value);
            var installFolder = Unescape(ManifestInstallDirectory.Match(text).Groups["value"].Value);
            var installDirectory = string.IsNullOrWhiteSpace(installFolder)
                ? string.Empty
                : Path.Combine(library, "steamapps", "common", installFolder);
            game = new SteamGameInfo(
                appId,
                string.IsNullOrWhiteSpace(name) ? $"Steam App {appId}" : LimitText(name, 256),
                installDirectory);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyCollection<string> GetSteamLibraries()
    {
        if (!TryGetSteamDirectory(out var steamDirectory) || steamDirectory is null)
        {
            return Array.Empty<string>();
        }

        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamDirectory };
        var libraryFile = Path.Combine(steamDirectory, "steamapps", "libraryfolders.vdf");
        try
        {
            if (File.Exists(libraryFile))
            {
                var text = File.ReadAllText(libraryFile);
                foreach (Match match in PathEntry.Matches(text).Cast<Match>().Concat(LegacyLibraryEntry.Matches(text).Cast<Match>()))
                {
                    var value = Unescape(match.Groups["value"].Value);
                    if (!string.IsNullOrWhiteSpace(value) && Path.IsPathFullyQualified(value))
                    {
                        libraries.Add(value);
                    }
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // The default library remains available.
        }

        return libraries;
    }

    private static bool TryGetSteamDirectory(out string? steamDirectory)
    {
        steamDirectory = null;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false);
            var value = key?.GetValue("SteamPath") as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            steamDirectory = Path.GetFullPath(value.Replace('/', Path.DirectorySeparatorChar));
            return Directory.Exists(steamDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or ArgumentException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static string Unescape(string value) => value.Replace("\\\\", "\\").Trim();

    private static string LimitText(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
