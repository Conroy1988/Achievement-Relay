using System.IO;
using System.Net;
using System.Net.Http;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed record AchievementCardArtwork(
    byte[]? HeroImageBytes,
    byte[]? AchievementIconBytes);

/// <summary>
/// Downloads optional, public presentation artwork without ever sharing an
/// OpenXBL key, Discord webhook, cookie, or other application credential.
/// Missing artwork is deliberately a normal result: the Collector Card has a
/// complete branded fallback of its own.
/// </summary>
public sealed class AchievementArtworkClient : IDisposable
{
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, (byte[] Bytes, DateTimeOffset At)> _cache = new();
    private long _cacheBytes;
    public long CachedBytes { get { lock (_cacheGate) return _cacheBytes; } }
    private const int MaximumArtworkBytes = 6 * 1024 * 1024;
    private static readonly TimeSpan ArtworkRequestTimeout = TimeSpan.FromSeconds(5);
    private static readonly HashSet<string> AllowedImageHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "images-eds-ssl.xboxlive.com",
        "images-eds.xboxlive.com",
        "dlassets-ssl.xboxlive.com",
        "dlassets.xboxlive.com",
        "store-images.s-microsoft.com",
        "store-images.microsoft.com",
        "cdn.akamai.steamstatic.com",
        "shared.akamai.steamstatic.com",
        "cdn.cloudflare.steamstatic.com",
        "shared.cloudflare.steamstatic.com"
    };

    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    public async Task<AchievementCardArtwork> GetAsync(
        AchievementEvent achievement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(achievement);

        var heroTask = DownloadAsync(achievement.HeroImageUrl, cancellationToken);
        var iconTask = achievement.ImageBytes is { Length: > 0 }
            ? Task.FromResult<byte[]?>(achievement.ImageBytes)
            : DownloadAsync(achievement.ImageUrl, cancellationToken);

        await Task.WhenAll(heroTask, iconTask);
        return new AchievementCardArtwork(
            await heroTask,
            await iconTask);
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<byte[]?> DownloadAsync(
        string? value,
        CancellationToken cancellationToken)
    {
        if (!TryValidateUri(value, out var uri) || uri is null)
        {
            return null;
        }
        lock (_cacheGate)
            if (_cache.TryGetValue(uri.AbsoluteUri, out var cached) && DateTimeOffset.UtcNow - cached.At < TimeSpan.FromMinutes(15))
                return cached.Bytes;

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ArtworkRequestTimeout);
            var artworkToken = timeout.Token;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                artworkToken);
            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentLength is > MaximumArtworkBytes)
            {
                return null;
            }

            await using var input = await response.Content.ReadAsStreamAsync(artworkToken);
            using var output = new MemoryStream();
            var buffer = new byte[64 * 1024];
            while (true)
            {
                var read = await input.ReadAsync(buffer, artworkToken);
                if (read == 0)
                {
                    break;
                }

                if (output.Length + read > MaximumArtworkBytes)
                {
                    return null;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), artworkToken);
            }

            var bytes = output.ToArray();
            if (!IsSupportedRasterImage(bytes)) return null;
            lock (_cacheGate)
            {
                if (_cache.Remove(uri.AbsoluteUri, out var old)) _cacheBytes -= old.Bytes.Length;
                while (_cache.Count > 0 && (_cacheBytes + bytes.Length > 24 * 1024 * 1024 || _cache.Count >= 32))
                { var first = _cache.MinBy(x => x.Value.At); _cache.Remove(first.Key); _cacheBytes -= first.Value.Bytes.Length; }
                _cache[uri.AbsoluteUri] = (bytes, DateTimeOffset.UtcNow); _cacheBytes += bytes.Length;
            }
            return bytes;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryValidateUri(string? value, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            candidate.Scheme != Uri.UriSchemeHttps ||
            !candidate.IsDefaultPort ||
            !string.IsNullOrEmpty(candidate.UserInfo) ||
            string.IsNullOrWhiteSpace(candidate.Host) ||
            !AllowedImageHosts.Contains(candidate.IdnHost))
        {
            return false;
        }

        uri = candidate;
        return true;
    }

    private static bool IsSupportedRasterImage(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 8 &&
        (bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
         (bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff));
}
