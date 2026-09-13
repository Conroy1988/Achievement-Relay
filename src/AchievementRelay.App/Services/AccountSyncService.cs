using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed class AccountSyncService(AppServices services)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AchievementRelay.AccountBaseline.v1");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private sealed record Snapshot(int Schema, JsonObject Settings, JournalEntry[] Journal, LibraryGame[] Library);
    private string BaselinePath => Path.Combine(services.Paths.DataDirectory, "account-baseline-" + services.AccountCloud.UserId + ".bin");

    public async Task<AppSettings> SyncAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var cloud = services.AccountCloud;
            if (!cloud.HasRecoveryKey) throw new InvalidOperationException("Enter the recovery key from your first device, or create one for a new account.");
            var localSettings = await services.SettingsStore.LoadAsync();
            var local = Capture(localSettings);
            JsonObject? baseline = null;
            if (File.Exists(BaselinePath))
            {
                var data = ProtectedData.Unprotect(await File.ReadAllBytesAsync(BaselinePath), Entropy, DataProtectionScope.CurrentUser);
                try { baseline = JsonNode.Parse(data)?.AsObject(); }
                finally { CryptographicOperations.ZeroMemory(data); }
            }
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var row = await cloud.ReadAsync();
                Snapshot? remote = null;
                if (row is not null)
                {
                    var data = cloud.Decrypt(row);
                    try { remote = JsonSerializer.Deserialize<Snapshot>(data, Json); }
                    finally { CryptographicOperations.ZeroMemory(data); }
                    if (remote?.Schema != 1 || remote.Settings is null || remote.Journal is null || remote.Library is null)
                        throw new InvalidDataException("Update Relay before opening this account profile.");
                }
                var merged = new Snapshot(1, remote is null ? local : AccountProfile.Merge(local, remote.Settings, baseline),
                    services.CompanionJournal.Snapshot.Concat(remote?.Journal ?? [])
                        .GroupBy(x => x.Achievement.Id).Select(g => g.MaxBy(x => x.UpdatedAt)!)
                        .OrderBy(x => x.ObservedAt).TakeLast(300)
                        .Select(x => x with { Achievement = Compact(x.Achievement) }).ToArray(),
                    services.CompanionLibrary.Snapshot.Concat(remote?.Library ?? [])
                        .GroupBy(x => x.Key).Select(g => g.MaxBy(x => x.ObservedAt)!)
                        .OrderBy(x => x.ObservedAt).TakeLast(100)
                        .Select(x => x with { History = x.History.Take(30).Select(Compact).ToArray() }).ToArray());
                // Bound history size without discarding the user's local collection.
                var bytes = JsonSerializer.SerializeToUtf8Bytes(merged, Json);
                while (bytes.Length > AccountCipher.MaximumPlaintextBytes && (merged.Library.Length > 0 || merged.Journal.Length > 0))
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    merged = merged.Library.Length > 0 ? merged with { Library = merged.Library.Skip(1).ToArray() }
                        : merged with { Journal = merged.Journal.Skip(1).ToArray() };
                    bytes = JsonSerializer.SerializeToUtf8Bytes(merged, Json);
                }
                bool written;
                try { written = remote is not null && JsonSerializer.Serialize(remote, Json) == JsonSerializer.Serialize(merged, Json)
                    || await cloud.WriteAsync(cloud.Encrypt(bytes), row?.Revision); }
                finally { CryptographicOperations.ZeroMemory(bytes); }
                if (!written) continue;
                // Do not overwrite settings edited while the network request was in flight.
                var current = await services.SettingsStore.LoadAsync();
                if (!JsonNode.DeepEquals(local, Capture(current)))
                    throw new InvalidOperationException("Settings changed during sync. Sync again to include those changes.");
                var updated = AccountProfile.Apply(current, merged.Settings);
                var webhook = merged.Settings["webhook"]?.GetValue<string>() ?? "";
                var apiKey = merged.Settings["openXblKey"]?.GetValue<string>() ?? "";
                updated = updated with
                {
                    ProtectedWebhookUrl = string.IsNullOrEmpty(webhook) ? "" : services.WebhookProtector.Protect(webhook),
                    ProtectedOpenXblApiKey = string.IsNullOrEmpty(apiKey) ? "" : services.WebhookProtector.ProtectOpenXblApiKey(apiKey),
                    SetupCompleted = current.SetupCompleted || !string.IsNullOrEmpty(webhook) && (updated.SteamEnabled || !string.IsNullOrEmpty(apiKey))
                };
                await services.CompanionJournal.MergeAccountHistoryAsync(merged.Journal);
                await services.CompanionLibrary.MergeAccountHistoryAsync(merged.Library);
                if (current.XboxUserId != updated.XboxUserId) await services.RelayCoordinator.StopAsync();
                if (!updated.SteamEnabled) await services.SteamMonitorCoordinator.StopAsync();
                if (!await services.SettingsStore.CompareAndSaveAccountAsync(current, updated))
                    throw new InvalidOperationException("Settings changed during sync. Sync again to include those changes.");
                if (current.XboxUserId != updated.XboxUserId) await services.SyncStateStore.ClearAsync();
                var baselineBytes = JsonSerializer.SerializeToUtf8Bytes(merged.Settings, Json);
                try
                {
                    await File.WriteAllBytesAsync(BaselinePath + ".tmp", ProtectedData.Protect(baselineBytes, Entropy, DataProtectionScope.CurrentUser));
                    File.Move(BaselinePath + ".tmp", BaselinePath, true);
                }
                finally { CryptographicOperations.ZeroMemory(baselineBytes); }
                return updated;
            }
            throw new InvalidOperationException("Another device is syncing. Try again shortly.");
        }
        finally { _gate.Release(); }
    }

    private JsonObject Capture(AppSettings settings) => AccountProfile.Capture(settings,
        services.WebhookProtector.TryUnprotect(settings.ProtectedWebhookUrl),
        services.WebhookProtector.TryUnprotectOpenXblApiKey(settings.ProtectedOpenXblApiKey));
    private static AchievementEvent Compact(AchievementEvent item) => item with
        { ImageBytes = null, ImageFileName = null, ImageContentType = null, IsCollectorCard = false };
}
