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

        var pendingFiles = paths.PendingInstallerSetupFiles
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (pendingFiles.Length == 0)
        {
            return new InstallerSetupImportResult(currentSettings, false, false, string.Empty);
        }

        var pendingFile = pendingFiles[0];
        PendingInstallerSetup? pendingSetup;
        try
        {
            var file = new FileInfo(pendingFile);
            if (file.Length is <= 0 or > MaximumPendingFileBytes)
            {
                DeletePendingSetupFiles(pendingFiles);
                return new InstallerSetupImportResult(
                    currentSettings,
                    true,
                    false,
                    "The optional installer setup data was empty or too large. Complete Guided setup in the app.");
            }

            await using var stream = new FileStream(
                pendingFile,
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
        catch (JsonException)
        {
            DeletePendingSetupFiles(pendingFiles);
            return new InstallerSetupImportResult(
                currentSettings,
                true,
                false,
                "The optional installer setup data was not recognized. Complete Guided setup in the app.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new InstallerSetupImportResult(
                currentSettings,
                true,
                false,
                "Achievement Relay could not read the optional installer setup data. It was kept securely so the next launch can retry.");
        }

        if (pendingSetup is null || pendingSetup.SchemaVersion != 1)
        {
            DeletePendingSetupFiles(pendingFiles);
            return new InstallerSetupImportResult(
                currentSettings,
                true,
                false,
                "The optional installer setup data was not recognized. Complete Guided setup in the app.");
        }

        var pendingApiKey = secretProtector.TryUnprotectOpenXblApiKey(pendingSetup.ProtectedOpenXblApiKey);
        var pendingWebhook = secretProtector.TryUnprotect(pendingSetup.ProtectedWebhookUrl);
        if (!OpenXblApiKeyValidator.TryNormalize(
                pendingApiKey,
                out var apiKey,
                out var keyError))
        {
            DeletePendingSetupFiles(pendingFiles);
            return new InstallerSetupImportResult(
                currentSettings,
                true,
                false,
                keyError ?? "The optional OpenXBL setting was invalid. Complete Guided setup in the app.");
        }

        if (!WebhookUrlValidator.TryNormalize(
                pendingWebhook,
                out var webhookUri,
                out var webhookError) ||
            webhookUri is null)
        {
            DeletePendingSetupFiles(pendingFiles);
            return new InstallerSetupImportResult(
                currentSettings,
                true,
                false,
                webhookError ?? "The optional Discord setting was invalid. Complete Guided setup in the app.");
        }

        var storedSettings = currentSettings with
        {
            SchemaVersion = AppSettings.CurrentSchemaVersion,
            ProtectedOpenXblApiKey = secretProtector.ProtectOpenXblApiKey(apiKey),
            ProtectedWebhookUrl = secretProtector.Protect(webhookUri.ToString()),
            SetupCompleted = false
        };

        try
        {
            // Persist both re-encrypted secrets before any network request. A failed or
            // interrupted OpenXBL/Discord test must never force the user to re-enter them.
            await settingsStore.SaveAsync(storedSettings, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new InstallerSetupImportResult(
                currentSettings,
                true,
                false,
                "Achievement Relay could not save the installer settings. The encrypted handoff was kept so the next launch can retry.");
        }

        DeletePendingSetupFiles(paths.PendingInstallerSetupFiles);

        var nextSettings = storedSettings;
        try
        {
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
                    "Installer settings were stored securely. Xbox and Discord were verified, and account monitoring is ready.");
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
                $"Installer settings were encrypted and saved, but verification needs attention: {string.Join(" ", details)} Complete Guided setup in the app; leave a secret field blank to reuse its stored value.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new InstallerSetupImportResult(
                storedSettings,
                true,
                false,
                "Installer settings were encrypted and saved before verification was interrupted. Open Guided setup and leave a secret field blank to retry the stored value.");
        }
    }

    private static void DeletePendingSetupFiles(IEnumerable<string> pendingFiles)
    {
        foreach (var pendingFile in pendingFiles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(pendingFile))
                {
                    continue;
                }

                using (var stream = new FileStream(
                           pendingFile,
                           FileMode.Create,
                           FileAccess.Write,
                           FileShare.None))
                {
                    stream.Flush(flushToDisk: true);
                }

                File.Delete(pendingFile);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A later launch will retry cleanup before it accepts another installer handoff.
            }
        }
    }

    private sealed record PendingInstallerSetup
    {
        public int SchemaVersion { get; init; }

        public string ProtectedOpenXblApiKey { get; init; } = string.Empty;

        public string ProtectedWebhookUrl { get; init; } = string.Empty;
    }
}
