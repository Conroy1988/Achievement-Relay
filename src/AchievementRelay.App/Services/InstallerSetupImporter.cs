using System.IO;
using System.Text.Json;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed record InstallerSetupImportResult(
    AppSettings Settings,
    bool Found,
    bool Completed,
    string Message);

public sealed class InstallerSetupImporter(
    AppPaths paths,
    SecureWebhookProtector secretProtector,
    SettingsStore settingsStore,
    XboxSyncStateStore syncStateStore,
    OpenXblClient openXblClient,
    DiscordWebhookClient webhookClient)
{
    private const long MaximumPendingFileBytes = 32 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<InstallerSetupImportResult> TryImportAsync(
        AppSettings currentSettings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentSettings);

        if (!File.Exists(paths.PendingInstallerSetupFile))
        {
            return new InstallerSetupImportResult(currentSettings, false, false, string.Empty);
        }

        PendingInstallerSetup? pendingSetup = null;
        string? readError = null;
        try
        {
            var file = new FileInfo(paths.PendingInstallerSetupFile);
            if (file.Length is <= 0 or > MaximumPendingFileBytes)
            {
                readError = "The optional installer setup data was empty or too large.";
            }
            else
            {
                await using var stream = new FileStream(
                    paths.PendingInstallerSetupFile,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None,
                    4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                pendingSetup = await JsonSerializer.DeserializeAsync<PendingInstallerSetup>(
                    stream,
                    JsonOptions,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            readError = "Achievement Relay could not read the optional installer setup data.";
        }
        finally
        {
            DeletePendingSetupFile();
        }

        if (pendingSetup is null || pendingSetup.SchemaVersion != 1)
        {
            return new InstallerSetupImportResult(
                currentSettings,
                true,
                false,
                readError ?? "The optional installer setup data was not recognized. Complete Guided setup in the app.");
        }

        var pendingApiKey = secretProtector.TryUnprotectOpenXblApiKey(pendingSetup.ProtectedOpenXblApiKey);
        var pendingWebhook = secretProtector.TryUnprotect(pendingSetup.ProtectedWebhookUrl);
        if (!OpenXblApiKeyValidator.TryNormalize(
                pendingApiKey,
                out var apiKey,
                out var keyError) ||
            !WebhookUrlValidator.TryNormalize(
                pendingWebhook,
                out var webhookUri,
                out var webhookError) ||
            webhookUri is null)
        {
            return new InstallerSetupImportResult(
                currentSettings,
                true,
                false,
                keyError ?? webhookError ?? "The optional installer settings were invalid. Complete Guided setup in the app.");
        }

        var nextSettings = currentSettings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            ProtectedOpenXblApiKey = secretProtector.ProtectOpenXblApiKey(apiKey),
            ProtectedWebhookUrl = secretProtector.Protect(webhookUri.ToString()),
            SetupCompleted = false
        };

        var accountResult = await openXblClient.GetAccountAsync(apiKey, cancellationToken);
        OpenXblTitleProgressResult? titleProgressResult = null;
        if (accountResult.Success && accountResult.Account is not null)
        {
            titleProgressResult = await openXblClient.GetTitleProgressAsync(
                apiKey,
                accountResult.Account.Xuid,
                cancellationToken);
            nextSettings = nextSettings with
            {
                XboxUserId = accountResult.Account.Xuid,
                XboxGamertag = accountResult.Account.Gamertag,
                DisplayName = string.IsNullOrWhiteSpace(nextSettings.DisplayName)
                    ? accountResult.Account.Gamertag
                    : nextSettings.DisplayName
            };

            if (titleProgressResult.Success && titleProgressResult.Titles is not null)
            {
                var state = await syncStateStore.LoadAsync(cancellationToken);
                if (state.BaselineUtc is null ||
                    !string.Equals(state.AccountXuid, accountResult.Account.Xuid, StringComparison.Ordinal))
                {
                    await syncStateStore.ResetAsync(
                        accountResult.Account.Xuid,
                        DateTimeOffset.UtcNow,
                        titleProgressResult.Titles,
                        cancellationToken);
                }
            }
        }

        var webhookResult = await webhookClient.SendAsync(
            webhookUri,
            DiscordWebhookPayloadFactory.CreateConnectionTest(nextSettings),
            cancellationToken);
        var completed = accountResult.Success &&
                        accountResult.Account is not null &&
                        titleProgressResult?.Success == true &&
                        webhookResult.Success;
        nextSettings = nextSettings with { SetupCompleted = completed };
        await settingsStore.SaveAsync(nextSettings, cancellationToken);

        if (completed)
        {
            return new InstallerSetupImportResult(
                nextSettings,
                true,
                true,
                "Installer setup imported securely. Xbox and Discord were verified, and account monitoring is ready.");
        }

        var details = new List<string>();
        if (!accountResult.Success)
        {
            details.Add(accountResult.Message);
        }
        else if (titleProgressResult?.Success != true)
        {
            details.Add(titleProgressResult?.Message ?? "The Xbox achievement feed could not be verified.");
        }

        if (!webhookResult.Success)
        {
            details.Add(webhookResult.Message);
        }

        return new InstallerSetupImportResult(
            nextSettings,
            true,
            false,
            $"Installer settings were encrypted and saved, but verification needs attention: {string.Join(" ", details)} Complete Guided setup in the app.");
    }

    private void DeletePendingSetupFile()
    {
        try
        {
            if (!File.Exists(paths.PendingInstallerSetupFile))
            {
                return;
            }

            using (var stream = new FileStream(
                       paths.PendingInstallerSetupFile,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Flush(flushToDisk: true);
            }

            File.Delete(paths.PendingInstallerSetupFile);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later launch will retry cleanup before it accepts another installer handoff.
        }
    }

    private sealed record PendingInstallerSetup
    {
        public int SchemaVersion { get; init; }

        public string ProtectedOpenXblApiKey { get; init; } = string.Empty;

        public string ProtectedWebhookUrl { get; init; } = string.Empty;
    }
}
