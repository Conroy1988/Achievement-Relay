using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

internal static class AccountTests
{
    public static void Run()
    {
        var local = new AppSettings { StartWithWindows = false, StartMinimized = false,
            ProtectedWebhookUrl = "local-dpapi", ProtectedOpenXblApiKey = "local-key", AchievementOverlayFollowWindowsMotion = true,
            Companion = new() { OverlayMonitor = "device-monitor", OverlayX = .9, OverlayY = .8, SharedDeliveryFolder = "device-folder" } };
        var profile = AccountProfile.Capture(local, "secret-webhook", "secret-api");
        if (profile.ToJsonString().Contains("local-dpapi") || profile.ToJsonString().Contains("device-folder") || profile.ContainsKey("startWithWindows"))
            throw new Exception("Device state leaked into account profile.");
        profile["displayName"] = "Shared name";
        profile["companion.soundVolume"] = 35;
        var restored = AccountProfile.Apply(local, profile);
        if (restored.DisplayName != "Shared name" || restored.Companion.SoundVolume != 35 || restored.StartWithWindows || restored.StartMinimized ||
            restored.Companion.OverlayMonitor != "device-monitor" || restored.Companion.OverlayX != .9 || restored.Companion.SharedDeliveryFolder != "device-folder" ||
            !restored.AchievementOverlayFollowWindowsMotion || restored.ProtectedWebhookUrl != "local-dpapi")
            throw new Exception("Portable settings did not preserve device settings.");
        var baseline = new JsonObject { ["a"] = 1, ["b"] = 1 };
        var changed = new JsonObject { ["a"] = 2, ["b"] = 1 };
        var remote = new JsonObject { ["a"] = 1, ["b"] = 3 };
        var merged = AccountProfile.Merge(changed, remote, baseline);
        if (merged["a"]!.GetValue<int>() != 2 || merged["b"]!.GetValue<int>() != 3)
            throw new Exception("Independent device edits were lost.");
        if (!JsonNode.DeepEquals(AccountProfile.Merge(changed, remote, null), remote))
            throw new Exception("A new device overwrote remote settings.");
        var pins = "companion.pinnedAchievements";
        var pinBase = new JsonObject { [pins] = new JsonArray("a", "b") };
        var pinLocal = new JsonObject { [pins] = new JsonArray("b", "c") };
        var pinRemote = new JsonObject { [pins] = new JsonArray("a", "b", "d") };
        var pinResult = AccountProfile.Merge(pinLocal, pinRemote, pinBase)[pins]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
        if (!pinResult.SequenceEqual(new[] { "b", "c", "d" })) throw new Exception("Independent pin changes were lost.");
        var id = Guid.NewGuid(); var key = AccountCipher.NewKey();
        var data = Encoding.UTF8.GetBytes(profile.ToJsonString());
        var encrypted = AccountCipher.Encrypt(data, key, id);
        if (!data.SequenceEqual(AccountCipher.Decrypt(encrypted, key, id))) throw new Exception("Encryption round trip failed.");
        if (encrypted == AccountCipher.Encrypt(data, key, id)) throw new Exception("Encryption reused a nonce.");
        MustReject(() => AccountCipher.Decrypt(encrypted, AccountCipher.NewKey(), id));
        MustReject(() => AccountCipher.Decrypt(encrypted, key, Guid.NewGuid()));
        var modified = Convert.FromBase64String(encrypted); modified[^1] ^= 1;
        MustReject(() => AccountCipher.Decrypt(Convert.ToBase64String(modified), key, id));
    }
    private static void MustReject(Action action)
    {
        try { action(); } catch (CryptographicException) { return; }
        throw new Exception("Cipher accepted wrong key, account, or tampered data.");
    }
}
