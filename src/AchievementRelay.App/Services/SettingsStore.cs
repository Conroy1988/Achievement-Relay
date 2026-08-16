using System.IO;
using System.Text.Json;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed class SettingsStore(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(paths.SettingsFile))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(paths.SettingsFile);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken)
                ?? new AppSettings();

            return settings.SchemaVersion < AppSettings.CurrentSchemaVersion
                ? settings with
                {
                    SchemaVersion = AppSettings.CurrentSchemaVersion,
                    // Schema 3 adds an optional, keyless Steam source. A user
                    // whose schema-2 Xbox/Discord setup is complete must not be
                    // forced through setup again merely because Steam arrived.
                    SetupCompleted = settings.SchemaVersion >= 2 && settings.SetupCompleted
                }
                : settings;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
        catch (IOException)
        {
            return new AppSettings();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var temporaryFile = string.Concat(paths.SettingsFile, ".tmp");
            await using (var stream = new FileStream(
                temporaryFile,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryFile, paths.SettingsFile, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
