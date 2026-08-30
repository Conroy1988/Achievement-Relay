namespace AchievementRelay.Core.Services;

public static class XboxPlatformClassifier
{
    private const int MaximumDeviceHints = 16;
    private const int MaximumDeviceHintLength = 64;

    public static string? Classify(
        string? earnedPlatform,
        IEnumerable<string>? availablePlatforms = null)
    {
        if (!string.IsNullOrWhiteSpace(earnedPlatform))
        {
            // An event-level platform is stronger than title availability,
            // but an unfamiliar value must still fail closed. Falling back to
            // a title's device list here could falsely attribute an unlock.
            return ClassifyToken(earnedPlatform);
        }

        var devices = NormalizeDevices(availablePlatforms);
        if (devices.Length == 0)
        {
            return null;
        }

        var classifications = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in devices)
        {
            var classification = ClassifyToken(device);
            if (classification is null)
            {
                // Mixed known/unknown title metadata is ambiguous. Do not
                // discard the unknown value and accidentally claim a device.
                return null;
            }

            classifications.Add(classification);
        }

        return classifications.Count == 1 ? classifications.Single() : null;
    }

    public static string ForDelivery(
        string? earnedPlatform,
        IEnumerable<string>? availablePlatforms = null) =>
        Classify(earnedPlatform, availablePlatforms) ?? "Xbox";

    public static string[] NormalizeDevices(params IEnumerable<string>?[] sources)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            if (source is null)
            {
                continue;
            }

            foreach (var value in source)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var trimmed = value.Trim();
                if (trimmed.Length > MaximumDeviceHintLength)
                {
                    trimmed = "Unknown";
                }

                if (!seen.Add(trimmed))
                {
                    continue;
                }

                if (normalized.Count < MaximumDeviceHints)
                {
                    normalized.Add(trimmed);
                    continue;
                }

                // Preserve the fact that provider evidence exceeded our
                // storage bound. Silently dropping a later conflicting token
                // could turn an ambiguous title into a specific platform.
                normalized[^1] = "Unknown";
                return normalized.ToArray();
            }
        }

        return normalized.ToArray();
    }

    private static string? ClassifyToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Trim().Length > MaximumDeviceHintLength)
        {
            return null;
        }

        var normalized = new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        if (normalized.Length == 0 || normalized.All(char.IsDigit))
        {
            return null;
        }

        if (normalized is "PC" or "XBOXPC" or "WINDOWS" or "WIN32" or "WIN64" or
            "WINDOWSDESKTOP" or "WINDOWS10" or "WINDOWS11")
        {
            return "Xbox PC";
        }

        if (normalized is "XBOX360" or "XENON")
        {
            return "Xbox 360";
        }

        if (normalized is "XBOXCONSOLE" or "XBOXONE" or "XBOXONEX" or "XBOXONES" or
            "XBOXSERIES" or "XBOXSERIESXS" or "SCARLETT" or "DURANGO")
        {
            return "Xbox Console";
        }

        return null;
    }
}
