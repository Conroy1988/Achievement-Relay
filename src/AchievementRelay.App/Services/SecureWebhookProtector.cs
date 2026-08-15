using System.Security.Cryptography;
using System.Text;

namespace AchievementRelay.App.Services;

public sealed class SecureWebhookProtector
{
    private static readonly byte[] WebhookEntropy = Encoding.UTF8.GetBytes("AchievementRelay.Webhook.v1");
    private static readonly byte[] OpenXblEntropy = Encoding.UTF8.GetBytes("AchievementRelay.OpenXBL.v1");

    public string Protect(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return Protect(value, WebhookEntropy);
    }

    public string? TryUnprotect(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            return Unprotect(protectedValue, WebhookEntropy);
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

    public string ProtectOpenXblApiKey(string value) => Protect(value, OpenXblEntropy);

    public string? TryUnprotectOpenXblApiKey(string? protectedValue)
    {
        if (string.IsNullOrWhiteSpace(protectedValue))
        {
            return null;
        }

        try
        {
            return Unprotect(protectedValue, OpenXblEntropy);
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

    private static string Protect(string value, byte[] entropy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var plaintext = Encoding.UTF8.GetBytes(value.Trim());
        try
        {
            var protectedBytes = ProtectedData.Protect(plaintext, entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static string Unprotect(string protectedValue, byte[] entropy)
    {
        var protectedBytes = Convert.FromBase64String(protectedValue);
        var plaintext = ProtectedData.Unprotect(protectedBytes, entropy, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
