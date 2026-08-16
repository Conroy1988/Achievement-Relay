namespace AchievementRelay.Core.Services;

public static class WebhookUrlValidator
{
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "discord.com",
        "ptb.discord.com",
        "canary.discord.com",
        "discordapp.com"
    };

    public static bool TryNormalize(string? value, out Uri? webhookUri, out string? error)
    {
        webhookUri = null;
        error = null;

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var candidate))
        {
            error = "Paste a complete Discord webhook URL.";
            return false;
        }

        if (candidate.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(candidate.Host))
        {
            error = "Only HTTPS webhook URLs hosted by Discord are accepted.";
            return false;
        }

        if (!candidate.IsDefaultPort || candidate.UserInfo.Length > 0 || candidate.Fragment.Length > 0)
        {
            error = "The Discord webhook URL contains unsupported connection details.";
            return false;
        }

        var segments = candidate.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var apiIndex = Array.FindIndex(segments, segment => segment.Equals("api", StringComparison.OrdinalIgnoreCase));
        var webhooksIndex = apiIndex + 1;
        if (apiIndex >= 0 && segments.Length > apiIndex + 1 &&
            segments[apiIndex + 1].StartsWith('v') &&
            int.TryParse(segments[apiIndex + 1].AsSpan(1), out _))
        {
            webhooksIndex++;
        }

        if (apiIndex != 0 || segments.Length != webhooksIndex + 3 ||
            !segments[webhooksIndex].Equals("webhooks", StringComparison.OrdinalIgnoreCase) ||
            !ulong.TryParse(segments[webhooksIndex + 1], out _) ||
            segments[webhooksIndex + 2].Length < 20)
        {
            error = "This does not look like a valid Discord channel webhook URL.";
            return false;
        }

        if (candidate.Host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase))
        {
            // Discord's legacy host can redirect to discord.com. Normalize it
            // before sending so the webhook client can keep redirects disabled
            // without breaking older copied URLs or forwarding the token.
            var canonical = new UriBuilder(candidate) { Host = "discord.com" };
            candidate = canonical.Uri;
        }

        webhookUri = candidate;
        return true;
    }

    public static string Redact(string? value)
    {
        if (!TryNormalize(value, out var uri, out _) || uri is null)
        {
            return "Not configured";
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var webhookId = segments.Length >= 3 ? segments[^2] : "unknown";
        return $"Discord webhook …/{webhookId}/••••••••";
    }
}
