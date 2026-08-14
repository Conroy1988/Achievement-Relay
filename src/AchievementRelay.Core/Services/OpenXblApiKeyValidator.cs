namespace AchievementRelay.Core.Services;

public static class OpenXblApiKeyValidator
{
    public static bool TryNormalize(string? value, out string normalized, out string? error)
    {
        normalized = value?.Trim() ?? string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(normalized))
        {
            error = "Paste an OpenXBL API key first.";
            return false;
        }

        if (normalized.Length > 512 || normalized.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            error = "The OpenXBL API key format is not valid.";
            normalized = string.Empty;
            return false;
        }

        return true;
    }
}
