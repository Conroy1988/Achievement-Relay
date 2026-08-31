namespace AchievementRelay.Core.Models;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string ProtectedWebhookUrl { get; init; } = string.Empty;

    public string ProtectedOpenXblApiKey { get; init; } = string.Empty;

    public string XboxUserId { get; init; } = string.Empty;

    public string XboxGamertag { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool StartWithWindows { get; init; } = true;

    public bool StartMinimized { get; init; } = true;

    public bool PostRareOnly { get; init; }

    /// <summary>
    /// Shows the compact, passive Signal Strip for a newly observed live
    /// achievement. The default keeps the feature enabled for both new and
    /// upgraded installations; users can explicitly opt out in Settings.
    /// </summary>
    public bool AchievementOverlayEnabled { get; init; } = true;

    /// <summary>
    /// Enables local, read-only Steam achievement monitoring. Steam does not
    /// require an API key; the companion waits for a Steam game and silently
    /// baselines that game's existing unlocks before it relays anything.
    /// </summary>
    public bool SteamEnabled { get; init; } = true;

    public bool IncludeRawDetailsWhenUncertain { get; init; } = true;

    public bool SetupCompleted { get; init; }

    public string DiscordUsername { get; init; } = "Achievement Relay";

    public int PollIntervalSeconds { get; init; } = 60;
}
