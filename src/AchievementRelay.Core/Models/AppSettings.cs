namespace AchievementRelay.Core.Models;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string ProtectedWebhookUrl { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public bool StartWithWindows { get; init; } = true;

    public bool StartMinimized { get; init; } = true;

    public bool PostRareOnly { get; init; }

    public bool IncludeRawDetailsWhenUncertain { get; init; } = true;

    public bool SetupCompleted { get; init; }

    public string DiscordUsername { get; init; } = "Achievement Relay";
}
