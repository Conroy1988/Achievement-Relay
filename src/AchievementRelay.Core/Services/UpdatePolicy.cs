using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AchievementRelay.Core.Models;

namespace AchievementRelay.Core.Services;

public static partial class UpdatePolicy
{
    public const int CurrentManifestSchemaVersion = 1;
    public const string InstallerAssetName = "AchievementRelay_Setup.exe";
    public const long MaximumInstallerSize = 1_073_741_824;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static UpdateManifest ParseManifest(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        UpdateManifest manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<UpdateManifest>(json, JsonOptions)
                ?? throw new InvalidDataException("The update manifest was empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The update manifest is not valid JSON.", exception);
        }

        if (manifest.SchemaVersion != CurrentManifestSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported update manifest schema {manifest.SchemaVersion}.");
        }

        var latestVersion = ParseVersion(manifest.Version, "release version");
        var latestPackageVersion = ParsePackageVersion(manifest.PackageVersion);
        var minimumVersion = ParseVersion(
            manifest.MinimumSupportedVersion,
            "minimum supported version");
        if (minimumVersion > latestVersion)
        {
            throw new InvalidDataException(
                "The minimum supported version cannot be newer than the release version.");
        }

        if (manifest.PublishedAtUtc == default || manifest.PublishedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("The update publication time must be an explicit UTC timestamp.");
        }

        if (manifest.Installer is null)
        {
            throw new InvalidDataException("The update manifest does not describe an installer.");
        }

        if (!string.Equals(
                manifest.Installer.AssetName,
                InstallerAssetName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The update manifest names an unexpected installer asset.");
        }

        if (!Sha256Pattern().IsMatch(manifest.Installer.Sha256))
        {
            throw new InvalidDataException("The update installer SHA-256 is invalid.");
        }

        if (manifest.Installer.Size is <= 0 or > MaximumInstallerSize)
        {
            throw new InvalidDataException("The update installer size is outside the supported limit.");
        }

        return manifest with
        {
            Version = FormatVersion(latestVersion),
            PackageVersion = FormatPackageVersion(latestPackageVersion),
            MinimumSupportedVersion = FormatVersion(minimumVersion),
            Installer = manifest.Installer with
            {
                Sha256 = manifest.Installer.Sha256.ToLowerInvariant()
            }
        };
    }

    public static UpdateDecision Evaluate(
        Version currentVersion,
        Version currentPackageVersion,
        UpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(currentVersion);
        ArgumentNullException.ThrowIfNull(currentPackageVersion);
        ArgumentNullException.ThrowIfNull(manifest);

        var normalizedCurrent = NormalizeVersion(currentVersion);
        var normalizedCurrentPackage = NormalizePackageVersion(currentPackageVersion);
        var latest = ParseVersion(manifest.Version, "release version");
        var latestPackage = ParsePackageVersion(manifest.PackageVersion);
        var minimum = ParseVersion(manifest.MinimumSupportedVersion, "minimum supported version");
        if (latest > normalizedCurrent && latestPackage <= normalizedCurrentPackage)
        {
            throw new InvalidDataException(
                "The newer release does not contain a Windows package version that can upgrade this installation.");
        }

        var updateAvailable = latest > normalizedCurrent ||
                              (latest == normalizedCurrent && latestPackage > normalizedCurrentPackage);
        var requirement = !updateAvailable
            ? UpdateRequirement.Current
            : normalizedCurrent < minimum
                ? UpdateRequirement.Required
                : UpdateRequirement.Optional;

        return new UpdateDecision(
            requirement,
            normalizedCurrent,
            normalizedCurrentPackage,
            latest,
            latestPackage,
            minimum);
    }

    public static Version ParseVersion(string value, string fieldName = "version")
    {
        if (!VersionPattern().IsMatch(value ?? string.Empty))
        {
            throw new InvalidDataException($"The {fieldName} must use numeric X.Y.Z format.");
        }

        var parts = value.Split('.');
        try
        {
            return new Version(
                int.Parse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture),
                int.Parse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture),
                int.Parse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture));
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException($"The {fieldName} is outside the supported numeric range.", exception);
        }
    }

    public static string FormatVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    public static Version ParsePackageVersion(string value)
    {
        if (!PackageVersionPattern().IsMatch(value ?? string.Empty))
        {
            throw new InvalidDataException("The package version must use numeric X.Y.Z.W format.");
        }

        try
        {
            return Version.Parse(value);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentException)
        {
            throw new InvalidDataException("The package version is outside the supported numeric range.", exception);
        }
    }

    public static string FormatPackageVersion(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var normalized = NormalizePackageVersion(version);
        return $"{normalized.Major}.{normalized.Minor}.{normalized.Build}.{normalized.Revision}";
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static Version NormalizePackageVersion(Version version) =>
        new(
            version.Major,
            version.Minor,
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageVersionPattern();

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
