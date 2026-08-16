using System.Text.Json.Serialization;

namespace AchievementRelay.Core.Models;

public sealed record UpdateManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = string.Empty;

    [JsonPropertyName("packageVersion")]
    public string PackageVersion { get; init; } = string.Empty;

    [JsonPropertyName("minimumSupportedVersion")]
    public string MinimumSupportedVersion { get; init; } = string.Empty;

    [JsonPropertyName("publishedAtUtc")]
    public DateTimeOffset PublishedAtUtc { get; init; }

    [JsonPropertyName("installer")]
    public UpdateInstallerAsset Installer { get; init; } = new();
}

public sealed record UpdateInstallerAsset
{
    [JsonPropertyName("assetName")]
    public string AssetName { get; init; } = string.Empty;

    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; init; }
}

public enum UpdateRequirement
{
    Current,
    Optional,
    Required
}

public sealed record UpdateDecision(
    UpdateRequirement Requirement,
    Version CurrentVersion,
    Version CurrentPackageVersion,
    Version LatestVersion,
    Version LatestPackageVersion,
    Version MinimumSupportedVersion);
