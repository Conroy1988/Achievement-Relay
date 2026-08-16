using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public enum AppUpdateStage
{
    NotChecked,
    Checking,
    Current,
    Available,
    Required,
    Downloading,
    ReadyToInstall,
    Failed
}

public sealed record AppUpdateSnapshot
{
    public AppUpdateStage Stage { get; init; } = AppUpdateStage.NotChecked;

    public UpdateRequirement? Requirement { get; init; }

    public string CurrentVersion { get; init; } = string.Empty;

    public string? LatestVersion { get; init; }

    public string Message { get; init; } = "Updates have not been checked yet.";

    public Uri? ReleasePage { get; init; }

    public DateTimeOffset? LastCheckedUtc { get; init; }

    public double? DownloadProgress { get; init; }

    public string? InstallerPath { get; init; }

    public bool HasUpdate => Requirement is UpdateRequirement.Optional or UpdateRequirement.Required;

    public bool IsRequired => Requirement == UpdateRequirement.Required;
}

public sealed record UpdateLaunchResult(bool Success, string Message, int? ProcessId = null);

public sealed class AppUpdateService : IDisposable
{
    private const int CacheSchemaVersion = 2;
    private const string Owner = "Conroy1988";
    private const string Repository = "Achievement-Relay";
    private const string PackageVersionMetadataName = "AchievementRelay.PackageVersion";
    private const string ManifestAssetName = "AchievementRelay_Update.json";
    private const string ManifestSignatureAssetName = "AchievementRelay_Update.sig";
    private const int MaximumReleaseResponseBytes = 512 * 1024;
    private const int MaximumCacheBytes = 256 * 1024;
    private const int MaximumManifestBytes = 64 * 1024;
    private const int MaximumManifestSignatureBytes = 64 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly Uri LatestReleaseApi = new(
        $"https://api.github.com/repos/{Owner}/{Repository}/releases/latest");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions CacheJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions GitHubJsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    private readonly AppPaths _paths;
    private readonly ActivityLog _activityLog;
    private readonly HttpClient _httpClient;
    private readonly Version _currentVersion;
    private readonly Version _currentPackageVersion;
    private readonly IReadOnlySet<string> _pinnedPublisherCertificates;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _snapshotGate = new();
    private readonly CancellationTokenSource _automaticChecksCancellation = new();
    private AppUpdateSnapshot _snapshot;
    private Task? _automaticChecksTask;
    private bool _disposed;

    public AppUpdateService(AppPaths paths, ActivityLog activityLog)
    {
        _paths = paths;
        _activityLog = activityLog;
        _currentVersion = NormalizeVersion(
            Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));
        _currentPackageVersion = ReadCurrentPackageVersion(_currentVersion);
        _pinnedPublisherCertificates = InstallerTrustVerifier.ReadPinnedPublisherCertificates();
        _snapshot = new AppUpdateSnapshot
        {
            CurrentVersion = UpdatePolicy.FormatVersion(_currentVersion)
        };

        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    public event EventHandler<AppUpdateSnapshot>? StateChanged;

    public AppUpdateSnapshot Snapshot
    {
        get
        {
            lock (_snapshotGate)
            {
                return _snapshot;
            }
        }
    }

    public bool IsUpdateRequired => Snapshot.IsRequired;

    public string CurrentPackageVersion => UpdatePolicy.FormatPackageVersion(_currentPackageVersion);

    public async Task<AppUpdateSnapshot> CheckAsync(
        bool force,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var cache = await LoadCacheAsync(cancellationToken);
            var cachedSnapshot = TryCreateSnapshot(cache);
            if (cachedSnapshot is not null)
            {
                Publish(cachedSnapshot);
                if (!force &&
                    cache is not null &&
                    DateTimeOffset.UtcNow - cache.LastCheckedUtc < CheckInterval)
                {
                    if (cachedSnapshot.Stage == AppUpdateStage.Current)
                    {
                        CleanupInstalledDownloads();
                    }
                    return cachedSnapshot;
                }
            }

            Publish((cachedSnapshot ?? Snapshot) with
            {
                Stage = AppUpdateStage.Checking,
                Message = "Checking the official GitHub release…",
                DownloadProgress = null
            });

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(CheckTimeout);
                var refreshed = await RefreshFromGitHubAsync(cache, timeout.Token);
                var result = TryCreateSnapshot(refreshed)
                    ?? throw new InvalidDataException("The refreshed update state was incomplete.");
                Publish(result);
                if (result.HasUpdate &&
                    (cachedSnapshot?.HasUpdate != true ||
                     !string.Equals(cachedSnapshot.LatestVersion, result.LatestVersion, StringComparison.Ordinal) ||
                     cachedSnapshot.Requirement != result.Requirement))
                {
                    if (result.IsRequired)
                    {
                        _activityLog.Warning($"Achievement Relay {result.LatestVersion} is required; monitoring is paused until it is installed.");
                    }
                    else
                    {
                        _activityLog.Info($"Achievement Relay {result.LatestVersion} is available from GitHub.");
                    }
                }
                if (result.Stage == AppUpdateStage.Current)
                {
                    CleanupInstalledDownloads();
                }
                return result;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return PublishCheckFailure(cachedSnapshot, "GitHub did not answer the update check in time.");
            }
            catch (Exception exception) when (exception is
                HttpRequestException or
                IOException or
                JsonException or
                InvalidDataException or
                UnauthorizedAccessException)
            {
                return PublishCheckFailure(cachedSnapshot, exception.Message);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<AppUpdateSnapshot> DownloadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        string? partialPath = null;
        using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        downloadCancellation.CancelAfter(DownloadTimeout);
        var downloadToken = downloadCancellation.Token;
        try
        {
            var cache = await LoadCacheAsync(downloadToken);
            var available = TryCreateSnapshot(cache);
            if (!TryValidateCachedState(
                    cache,
                    out _,
                    out _,
                    out var manifest,
                    out var installerUri) ||
                manifest is null ||
                installerUri is null ||
                available is null ||
                !available.HasUpdate)
            {
                throw new InvalidOperationException("No verified update is available to download.");
            }

            var versionDirectory = Path.Combine(_paths.UpdatesDirectory, manifest.Version);
            Directory.CreateDirectory(versionDirectory);
            var installerPath = Path.Combine(versionDirectory, UpdatePolicy.InstallerAssetName);

            if (File.Exists(installerPath))
            {
                var existingTrust = await VerifyInstallerAsync(installerPath, manifest, downloadToken);
                if (existingTrust.Success)
                {
                    var ready = available with
                    {
                        Stage = AppUpdateStage.ReadyToInstall,
                        Message = $"Version {manifest.Version} is verified and ready to install.",
                        DownloadProgress = 1,
                        InstallerPath = installerPath
                    };
                    Publish(ready);
                    return ready;
                }

                File.Delete(installerPath);
            }

            partialPath = installerPath + ".partial";
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            Publish(available with
            {
                Stage = AppUpdateStage.Downloading,
                Message = $"Downloading Achievement Relay {manifest.Version}…",
                DownloadProgress = 0,
                InstallerPath = null
            });

            using var response = await SendGetWithRedirectsAsync(
                installerUri,
                configureHeaders: null,
                downloadToken);
            EnsureSuccess(response, "GitHub update download");
            if (response.Content.Headers.ContentLength is long contentLength &&
                contentLength != manifest.Installer.Size)
            {
                throw new InvalidDataException("GitHub returned an unexpected installer size.");
            }

            await using (var source = await response.Content.ReadAsStreamAsync(downloadToken))
            await using (var destination = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[128 * 1024];
                long total = 0;
                var lastReported = Stopwatch.StartNew();
                while (true)
                {
                    var read = await source.ReadAsync(buffer, downloadToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > manifest.Installer.Size ||
                        total > UpdatePolicy.MaximumInstallerSize)
                    {
                        throw new InvalidDataException("The installer exceeded its declared size.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), downloadToken);
                    if (lastReported.Elapsed >= TimeSpan.FromMilliseconds(250))
                    {
                        Publish(Snapshot with
                        {
                            Stage = AppUpdateStage.Downloading,
                            DownloadProgress = (double)total / manifest.Installer.Size,
                            Message = $"Downloading Achievement Relay {manifest.Version}… {FormatBytes(total)} of {FormatBytes(manifest.Installer.Size)}"
                        });
                        lastReported.Restart();
                    }
                }

                await destination.FlushAsync(downloadToken);
                if (total != manifest.Installer.Size)
                {
                    throw new InvalidDataException("The installer download ended before its declared size.");
                }
            }

            var trust = await VerifyInstallerAsync(partialPath, manifest, downloadToken);
            if (!trust.Success)
            {
                throw new InvalidDataException(trust.Message);
            }

            File.Move(partialPath, installerPath, true);
            partialPath = null;
            var completed = available with
            {
                Stage = AppUpdateStage.ReadyToInstall,
                Message = $"Version {manifest.Version} passed manifest, hash and publisher verification.",
                DownloadProgress = 1,
                InstallerPath = installerPath
            };
            Publish(completed);
            _activityLog.Success($"Achievement Relay {manifest.Version} downloaded and verified.");
            CleanupOldDownloads(manifest.Version);
            return completed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var failed = Snapshot with
            {
                Stage = AppUpdateStage.Failed,
                Message = "The update download timed out. Check the connection and try again.",
                DownloadProgress = null,
                InstallerPath = null
            };
            Publish(failed);
            _activityLog.Warning(failed.Message);
            return failed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            HttpRequestException or
            IOException or
            InvalidDataException or
            InvalidOperationException or
            UnauthorizedAccessException)
        {
            var failed = Snapshot with
            {
                Stage = AppUpdateStage.Failed,
                Message = $"The update was not installed: {exception.Message}",
                DownloadProgress = null,
                InstallerPath = null
            };
            Publish(failed);
            _activityLog.Warning(failed.Message);
            return failed;
        }
        finally
        {
            if (partialPath is not null)
            {
                TryDeleteFile(partialPath);
            }

            _operationGate.Release();
        }
    }

    public async Task<UpdateLaunchResult> LaunchInstallerAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var cache = await LoadCacheAsync(cancellationToken);
            var current = Snapshot;
            var installerPath = current.InstallerPath;
            if (!TryValidateCachedState(
                    cache,
                    out _,
                    out _,
                    out var manifest,
                    out _) ||
                manifest is null ||
                !current.HasUpdate ||
                string.IsNullOrWhiteSpace(installerPath))
            {
                return new UpdateLaunchResult(false, "The verified update is not ready yet.");
            }

            var trust = await VerifyInstallerAsync(
                installerPath,
                manifest,
                cancellationToken);
            if (!trust.Success)
            {
                TryDeleteFile(installerPath);
                Publish(current with
                {
                    Stage = AppUpdateStage.Failed,
                    Message = $"The update was not started: {trust.Message}",
                    DownloadProgress = null,
                    InstallerPath = null
                });
                return new UpdateLaunchResult(false, trust.Message);
            }

            var startInfo = new ProcessStartInfo(installerPath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath)
                    ?? _paths.UpdatesDirectory
            };
            startInfo.ArgumentList.Add("/UPDATE=1");
            startInfo.ArgumentList.Add($"/TARGETVERSION={manifest.Version}");
            startInfo.ArgumentList.Add($"/CURRENTVERSION={UpdatePolicy.FormatVersion(_currentVersion)}");
            var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows did not start the verified updater.");
            _activityLog.Info($"Starting the verified Achievement Relay {manifest.Version} updater.");
            return new UpdateLaunchResult(true, "The verified updater was started.", process.Id);
        }
        catch (Exception exception) when (exception is
            InvalidOperationException or
            IOException or
            UnauthorizedAccessException or
            System.ComponentModel.Win32Exception)
        {
            _activityLog.Warning($"The verified updater could not be started: {exception.Message}");
            return new UpdateLaunchResult(false, exception.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public void StartAutomaticChecks()
    {
        ThrowIfDisposed();
        if (_automaticChecksTask is not null)
        {
            return;
        }

        _automaticChecksTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(CheckInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(_automaticChecksCancellation.Token))
                {
                    await CheckAsync(force: false, _automaticChecksCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Normal application shutdown.
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _automaticChecksCancellation.Cancel();
        _httpClient.Dispose();
    }

    private async Task<PersistedUpdateState> RefreshFromGitHubAsync(
        PersistedUpdateState? cache,
        CancellationToken cancellationToken)
    {
        var response = await GetLatestReleaseResponseAsync(cache?.ETag, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            response.Dispose();
            if (TryCreateSnapshot(cache) is null || cache is null)
            {
                response = await GetLatestReleaseResponseAsync(null, cancellationToken);
            }
            else
            {
                var refreshedCache = cache with { LastCheckedUtc = DateTimeOffset.UtcNow };
                await SaveCacheAsync(refreshedCache, cancellationToken);
                return refreshedCache;
            }
        }

        using (response)
        {
            EnsureSuccess(response, "GitHub release check");
            var responseJson = await ReadBoundedTextAsync(
                response.Content,
                MaximumReleaseResponseBytes,
                cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(responseJson, GitHubJsonOptions)
                ?? throw new InvalidDataException("GitHub returned an empty release record.");
            if (release.Draft || release.Prerelease || release.Assets is null)
            {
                throw new InvalidDataException("GitHub returned a non-stable release as latest.");
            }

            var latestVersion = ParseTagVersion(release.TagName);
            var releasePage = ParseOfficialReleasePage(release.HtmlUrl);
            var etag = response.Headers.ETag?.ToString();
            if (latestVersion < _currentVersion)
            {
                var currentState = new PersistedUpdateState
                {
                    SchemaVersion = CacheSchemaVersion,
                    LastCheckedUtc = DateTimeOffset.UtcNow,
                    ETag = etag,
                    LatestVersion = UpdatePolicy.FormatVersion(latestVersion),
                    ReleasePageUrl = releasePage.ToString()
                };
                await SaveCacheAsync(currentState, cancellationToken);
                return currentState;
            }

            var manifestAsset = GetSingleAsset(release.Assets, ManifestAssetName);
            var manifestSignatureAsset = GetSingleAsset(release.Assets, ManifestSignatureAssetName);
            var installerAsset = GetSingleAsset(release.Assets, UpdatePolicy.InstallerAssetName);
            if (manifestAsset.Size is <= 0 or > MaximumManifestBytes)
            {
                throw new InvalidDataException("The GitHub update manifest has an unexpected size.");
            }
            if (manifestSignatureAsset.Size is <= 0 or > MaximumManifestSignatureBytes)
            {
                throw new InvalidDataException("The GitHub update manifest signature has an unexpected size.");
            }

            var manifestUri = ParseOfficialAssetUri(manifestAsset.BrowserDownloadUrl, ManifestAssetName);
            var manifestSignatureUri = ParseOfficialAssetUri(
                manifestSignatureAsset.BrowserDownloadUrl,
                ManifestSignatureAssetName);
            var installerUri = ParseOfficialAssetUri(
                installerAsset.BrowserDownloadUrl,
                UpdatePolicy.InstallerAssetName);
            using var manifestResponse = await SendGetWithRedirectsAsync(
                manifestUri,
                configureHeaders: null,
                cancellationToken);
            EnsureSuccess(manifestResponse, "GitHub update manifest download");
            var manifestBytes = await ReadBoundedBytesAsync(
                manifestResponse.Content,
                MaximumManifestBytes,
                cancellationToken);
            using var signatureResponse = await SendGetWithRedirectsAsync(
                manifestSignatureUri,
                configureHeaders: null,
                cancellationToken);
            EnsureSuccess(signatureResponse, "GitHub update manifest signature download");
            var signatureBytes = await ReadBoundedBytesAsync(
                signatureResponse.Content,
                MaximumManifestSignatureBytes,
                cancellationToken);
            var manifest = ParseVerifiedManifest(manifestBytes, signatureBytes);
            if (!string.Equals(
                    manifest.Version,
                    UpdatePolicy.FormatVersion(latestVersion),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException("The update manifest version does not match its GitHub release tag.");
            }

            if (installerAsset.Size != manifest.Installer.Size)
            {
                throw new InvalidDataException("The update manifest size does not match the GitHub installer asset.");
            }

            _ = UpdatePolicy.Evaluate(_currentVersion, _currentPackageVersion, manifest);

            var updateState = new PersistedUpdateState
            {
                SchemaVersion = CacheSchemaVersion,
                LastCheckedUtc = DateTimeOffset.UtcNow,
                ETag = etag,
                LatestVersion = manifest.Version,
                ReleasePageUrl = releasePage.ToString(),
                InstallerDownloadUrl = installerUri.ToString(),
                InstallerAssetSize = installerAsset.Size,
                ManifestBase64 = Convert.ToBase64String(manifestBytes),
                ManifestSignatureBase64 = Convert.ToBase64String(signatureBytes)
            };
            await SaveCacheAsync(updateState, cancellationToken);
            return updateState;
        }
    }

    private async Task<HttpResponseMessage> GetLatestReleaseResponseAsync(
        string? etag,
        CancellationToken cancellationToken) =>
        await SendGetWithRedirectsAsync(
            LatestReleaseApi,
            headers =>
            {
                headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
                headers.Add("X-GitHub-Api-Version", "2022-11-28");
                if (!string.IsNullOrWhiteSpace(etag) && EntityTagHeaderValue.TryParse(etag, out var parsedEtag))
                {
                    headers.IfNoneMatch.Add(parsedEtag);
                }
            },
            cancellationToken);

    private async Task<HttpResponseMessage> SendGetWithRedirectsAsync(
        Uri uri,
        Action<HttpRequestHeaders>? configureHeaders,
        CancellationToken cancellationToken)
    {
        var current = uri;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            EnsureAllowedGitHubUri(current);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd($"AchievementRelay/{UpdatePolicy.FormatVersion(_currentVersion)}");
            configureHeaders?.Invoke(request.Headers);
            var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!IsRedirect(response.StatusCode))
            {
                return response;
            }

            var redirectLocation = response.Headers.Location;
            if (redirect == MaximumRedirects || redirectLocation is null)
            {
                response.Dispose();
                throw new HttpRequestException("GitHub returned an invalid update redirect.");
            }

            current = redirectLocation.IsAbsoluteUri
                ? redirectLocation
                : new Uri(current, redirectLocation);
            response.Dispose();
        }

        throw new HttpRequestException("GitHub returned too many update redirects.");
    }

    private AppUpdateSnapshot? TryCreateSnapshot(PersistedUpdateState? cache)
    {
        if (!TryValidateCachedState(
                cache,
                out var latest,
                out var releasePage,
                out var validatedManifest,
                out _))
        {
            return null;
        }

        if (latest < _currentVersion)
        {
            return new AppUpdateSnapshot
            {
                Stage = AppUpdateStage.Current,
                Requirement = UpdateRequirement.Current,
                CurrentVersion = UpdatePolicy.FormatVersion(_currentVersion),
                LatestVersion = UpdatePolicy.FormatVersion(latest),
                Message = "Achievement Relay is up to date.",
                ReleasePage = releasePage,
                LastCheckedUtc = cache!.LastCheckedUtc
            };
        }

        if (validatedManifest is null)
        {
            return null;
        }

        UpdateDecision decision;
        try
        {
            decision = UpdatePolicy.Evaluate(
                _currentVersion,
                _currentPackageVersion,
                validatedManifest);
        }
        catch (InvalidDataException)
        {
            return null;
        }
        if (decision.Requirement == UpdateRequirement.Current)
        {
            return new AppUpdateSnapshot
            {
                Stage = AppUpdateStage.Current,
                Requirement = UpdateRequirement.Current,
                CurrentVersion = UpdatePolicy.FormatVersion(_currentVersion),
                LatestVersion = validatedManifest.Version,
                Message = "Achievement Relay is up to date.",
                ReleasePage = releasePage,
                LastCheckedUtc = cache!.LastCheckedUtc
            };
        }
        var required = decision.Requirement == UpdateRequirement.Required;
        return new AppUpdateSnapshot
        {
            Stage = required ? AppUpdateStage.Required : AppUpdateStage.Available,
            Requirement = decision.Requirement,
            CurrentVersion = UpdatePolicy.FormatVersion(_currentVersion),
            LatestVersion = validatedManifest.Version,
            Message = required
                ? $"Version {validatedManifest.Version} is required. Achievement monitoring is paused until it is installed."
                : $"Version {validatedManifest.Version} is available from the official GitHub release.",
            ReleasePage = releasePage,
            LastCheckedUtc = cache!.LastCheckedUtc
        };
    }

    private bool TryValidateCachedState(
        PersistedUpdateState? cache,
        out Version latest,
        out Uri releasePage,
        out UpdateManifest? manifest,
        out Uri? installerUri)
    {
        latest = new Version(0, 0, 0);
        releasePage = null!;
        manifest = null;
        installerUri = null;
        if (cache is null ||
            cache.SchemaVersion != CacheSchemaVersion ||
            cache.LastCheckedUtc == default ||
            cache.LastCheckedUtc > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return false;
        }

        try
        {
            latest = UpdatePolicy.ParseVersion(cache.LatestVersion, "cached release version");
            releasePage = ParseOfficialReleasePage(cache.ReleasePageUrl);
            if (latest < _currentVersion)
            {
                return true;
            }

            var manifestBytes = Convert.FromBase64String(cache.ManifestBase64);
            var signatureBytes = Convert.FromBase64String(cache.ManifestSignatureBase64);
            if (manifestBytes.Length is <= 0 or > MaximumManifestBytes ||
                signatureBytes.Length is <= 0 or > MaximumManifestSignatureBytes)
            {
                return false;
            }

            manifest = ParseVerifiedManifest(manifestBytes, signatureBytes);
            if (!string.Equals(
                    manifest.Version,
                    UpdatePolicy.FormatVersion(latest),
                    StringComparison.Ordinal) ||
                cache.InstallerAssetSize != manifest.Installer.Size)
            {
                return false;
            }

            installerUri = ParseOfficialAssetUri(
                cache.InstallerDownloadUrl,
                UpdatePolicy.InstallerAssetName);
            return true;
        }
        catch (Exception exception) when (exception is
            FormatException or
            InvalidDataException or
            JsonException or
            UriFormatException or
            InvalidOperationException or
            NullReferenceException)
        {
            manifest = null;
            installerUri = null;
            return false;
        }
    }

    private AppUpdateSnapshot PublishCheckFailure(
        AppUpdateSnapshot? cachedSnapshot,
        string detail)
    {
        if (cachedSnapshot?.IsRequired == true)
        {
            var retained = cachedSnapshot with
            {
                Message = $"{cachedSnapshot.Message} GitHub could not refresh the policy: {detail}"
            };
            Publish(retained);
            _activityLog.Warning("The required update policy could not be refreshed; the last verified requirement remains active.");
            return retained;
        }

        if (cachedSnapshot?.HasUpdate == true)
        {
            var retained = cachedSnapshot with
            {
                Message = $"{cachedSnapshot.Message} The latest check could not be refreshed."
            };
            Publish(retained);
            _activityLog.Warning($"The update check could not be refreshed: {detail}");
            return retained;
        }

        var failed = new AppUpdateSnapshot
        {
            Stage = AppUpdateStage.Failed,
            CurrentVersion = UpdatePolicy.FormatVersion(_currentVersion),
            Message = $"Could not check GitHub for updates: {detail}",
            LastCheckedUtc = cachedSnapshot?.LastCheckedUtc,
            ReleasePage = cachedSnapshot?.ReleasePage
        };
        Publish(failed);
        _activityLog.Warning(failed.Message);
        return failed;
    }

    private UpdateManifest ParseVerifiedManifest(
        byte[] manifestBytes,
        byte[] signatureBytes)
    {
        var verification = UpdateManifestSignatureVerifier.Verify(
            manifestBytes,
            signatureBytes,
            _pinnedPublisherCertificates);
        if (!verification.IsValid ||
            verification.CertificateNotBeforeUtc is not DateTimeOffset notBefore ||
            verification.CertificateNotAfterUtc is not DateTimeOffset notAfter)
        {
            throw new InvalidDataException(verification.Message);
        }

        var manifest = UpdatePolicy.ParseManifest(DecodeStrictUtf8(manifestBytes));
        var clockTolerance = TimeSpan.FromMinutes(5);
        if (manifest.PublishedAtUtc < notBefore - clockTolerance ||
            manifest.PublishedAtUtc > notAfter + clockTolerance ||
            manifest.PublishedAtUtc > DateTimeOffset.UtcNow + clockTolerance)
        {
            throw new InvalidDataException(
                "The signed update publication time is outside the publisher certificate validity window.");
        }

        return manifest;
    }

    private async Task<InstallerVerificationResult> VerifyInstallerAsync(
        string path,
        UpdateManifest manifest,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return new InstallerVerificationResult(false, "The downloaded installer is missing.");
        }

        var file = new FileInfo(path);
        if (file.Length != manifest.Installer.Size)
        {
            return new InstallerVerificationResult(false, "The downloaded installer size does not match the release manifest.");
        }

        try
        {
            var versionInfo = FileVersionInfo.GetVersionInfo(path);
            if (!string.Equals(versionInfo.ProductVersion, manifest.Version, StringComparison.Ordinal) ||
                !string.Equals(versionInfo.FileVersion, manifest.PackageVersion, StringComparison.Ordinal))
            {
                return new InstallerVerificationResult(false, "The installer version does not match the release manifest.");
            }
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception)
        {
            return new InstallerVerificationResult(false, "Windows could not read the installer version.");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        var actualHash = Convert.ToHexString(hash).ToLowerInvariant();
        if (!string.Equals(actualHash, manifest.Installer.Sha256, StringComparison.Ordinal))
        {
            return new InstallerVerificationResult(false, "The downloaded installer failed SHA-256 verification.");
        }

        var trust = await Task.Run(
            () => InstallerTrustVerifier.Verify(path, _pinnedPublisherCertificates),
            cancellationToken);
        return trust.IsTrusted
            ? new InstallerVerificationResult(true, trust.Message)
            : new InstallerVerificationResult(false, trust.Message);
    }

    private async Task<PersistedUpdateState?> LoadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.UpdateStateFile))
        {
            return null;
        }

        try
        {
            var cacheFile = new FileInfo(_paths.UpdateStateFile);
            if (cacheFile.Length is <= 0 or > MaximumCacheBytes)
            {
                return null;
            }

            await using var stream = File.OpenRead(_paths.UpdateStateFile);
            return await JsonSerializer.DeserializeAsync<PersistedUpdateState>(
                stream,
                CacheJsonOptions,
                cancellationToken);
        }
        catch (Exception exception) when (exception is
            IOException or
            JsonException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task SaveCacheAsync(
        PersistedUpdateState state,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_paths.DataDirectory);
        var temporaryPath = _paths.UpdateStateFile + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, state, CacheJsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _paths.UpdateStateFile, true);
    }

    private void CleanupOldDownloads(string currentVersion)
    {
        try
        {
            if (!Directory.Exists(_paths.UpdatesDirectory))
            {
                return;
            }

            foreach (var directory in Directory.EnumerateDirectories(_paths.UpdatesDirectory))
            {
                if (!string.Equals(
                        Path.GetFileName(directory),
                        currentVersion,
                        StringComparison.Ordinal))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _activityLog.Warning("An obsolete update download could not be removed.");
        }
    }

    private void CleanupInstalledDownloads()
    {
        try
        {
            if (Directory.Exists(_paths.UpdatesDirectory))
            {
                Directory.Delete(_paths.UpdatesDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _activityLog.Warning("A completed update download could not be removed.");
        }
    }

    private void Publish(AppUpdateSnapshot snapshot)
    {
        lock (_snapshotGate)
        {
            _snapshot = snapshot;
        }

        StateChanged?.Invoke(this, snapshot);
    }

    private static GitHubReleaseAsset GetSingleAsset(
        IReadOnlyList<GitHubReleaseAsset> assets,
        string expectedName)
    {
        var matches = assets
            .Where(asset => string.Equals(asset.Name, expectedName, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidDataException(
                $"The GitHub release must contain exactly one {expectedName} asset.");
    }

    private static Version ParseTagVersion(string tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName) || !tagName.StartsWith('v'))
        {
            throw new InvalidDataException("The GitHub release tag does not use vX.Y.Z format.");
        }

        return UpdatePolicy.ParseVersion(tagName[1..], "GitHub release tag");
    }

    private static Uri ParseOfficialReleasePage(string value)
    {
        var uri = ParseHttpsUri(value, "GitHub release page");
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                $"/{Owner}/{Repository}/releases/tag/",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub returned an unexpected release page.");
        }

        return uri;
    }

    private static Uri ParseOfficialAssetUri(string value, string expectedAssetName)
    {
        var uri = ParseHttpsUri(value, "GitHub release asset");
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.StartsWith(
                $"/{Owner}/{Repository}/releases/download/",
                StringComparison.OrdinalIgnoreCase) ||
            !uri.AbsolutePath.EndsWith(
                "/" + expectedAssetName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("GitHub returned an unexpected release asset URL.");
        }

        return uri;
    }

    private static Uri ParseHttpsUri(string value, string description)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidDataException($"The {description} is not a safe HTTPS URL.");
        }

        return uri;
    }

    private static void EnsureAllowedGitHubUri(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !(string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(uri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The update download left GitHub's approved HTTPS hosts.");
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.MovedPermanently or
        HttpStatusCode.Found or
        HttpStatusCode.SeeOther or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var rateLimited = (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) &&
                          response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) &&
                          remaining.Contains("0", StringComparer.Ordinal);
        throw new HttpRequestException(
            rateLimited
                ? "GitHub's public update-check allowance is temporarily exhausted. Try again later."
                : $"{operation} failed with HTTP {(int)response.StatusCode}.");
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken) =>
        DecodeStrictUtf8(await ReadBoundedBytesAsync(content, maximumBytes, cancellationToken));

    private static async Task<byte[]> ReadBoundedBytesAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length && length > maximumBytes)
        {
            throw new InvalidDataException("GitHub returned an oversized update response.");
        }

        await using var source = await content.ReadAsStreamAsync(cancellationToken);
        using var destination = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (destination.Length + read > maximumBytes)
            {
                throw new InvalidDataException("GitHub returned an oversized update response.");
            }

            destination.Write(buffer, 0, read);
        }

        return destination.ToArray();
    }

    private static string DecodeStrictUtf8(byte[] bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("GitHub returned invalid UTF-8 update data.", exception);
        }
    }

    private static string FormatBytes(long bytes)
    {
        const double mebibyte = 1024d * 1024d;
        return bytes >= mebibyte
            ? $"{bytes / mebibyte:0.0} MiB"
            : $"{bytes / 1024d:0.0} KiB";
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static Version ReadCurrentPackageVersion(Version fallbackVersion)
    {
        var value = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Key, PackageVersionMetadataName, StringComparison.Ordinal))?
            .Value;
        try
        {
            return UpdatePolicy.ParsePackageVersion(value ?? string.Empty);
        }
        catch (InvalidDataException)
        {
            return new Version(
                fallbackVersion.Major,
                fallbackVersion.Minor,
                Math.Max(fallbackVersion.Build, 0),
                Math.Max(fallbackVersion.Revision, 0));
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A later updater run will overwrite or remove the partial file.
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed record InstallerVerificationResult(bool Success, string Message);

    private sealed record PersistedUpdateState
    {
        public int SchemaVersion { get; init; }

        public DateTimeOffset LastCheckedUtc { get; init; }

        public string? ETag { get; init; }

        public string LatestVersion { get; init; } = string.Empty;

        public string ReleasePageUrl { get; init; } = string.Empty;

        public string InstallerDownloadUrl { get; init; } = string.Empty;

        public long InstallerAssetSize { get; init; }

        public string ManifestBase64 { get; init; } = string.Empty;

        public string ManifestSignatureBase64 { get; init; } = string.Empty;
    }

    private sealed record GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; init; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; init; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }

        [JsonPropertyName("assets")]
        public IReadOnlyList<GitHubReleaseAsset> Assets { get; init; } = [];
    }

    private sealed record GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; init; } = string.Empty;
    }
}
