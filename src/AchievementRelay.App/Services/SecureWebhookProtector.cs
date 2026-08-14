using System.Security.Cryptography;
using System.Text;

namespace AchievementRelay.App.Services;

public sealed class SecureWebhookProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AchievementRelay.Webhook.v1");

    public string Protect(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var plaintext = Encoding.UTF8.GetBytes(value.Trim());
        var protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
        CryptographicOperations.ZeroMemory(plaintext);
        return Convert.ToBase64String(protectedBytes);
    }

    public string? TryUnprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedValue);
            var plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var value = Encoding.UTF8.GetString(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);
            return value;
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
