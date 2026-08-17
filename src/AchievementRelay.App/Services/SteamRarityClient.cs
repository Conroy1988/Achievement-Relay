using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed class SteamRarityClient : IDisposable
{
    private readonly HttpClient _httpClient = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri("https://api.steampowered.com/"),
        Timeout = TimeSpan.FromSeconds(10),
        MaxResponseContentBufferSize = 1024 * 1024
    };
    private readonly Dictionary<uint, Task<IReadOnlyDictionary<string, double>>> _cache = [];
    private readonly object _cacheGate = new();

    public Task<IReadOnlyDictionary<string, double>> GetAsync(uint appId, CancellationToken cancellationToken = default)
    {
        lock (_cacheGate)
        {
            if (_cache.TryGetValue(appId, out var existing))
            {
                return existing.WaitAsync(cancellationToken);
            }

            // A caller cancellation must not poison the shared per-game cache.
            // The HttpClient timeout still bounds the provider request.
            var created = FetchAsync(appId, CancellationToken.None);
            _cache[appId] = created;
            return created.WaitAsync(cancellationToken);
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<IReadOnlyDictionary<string, double>> FetchAsync(
        uint appId,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"ISteamUserStats/GetGlobalAchievementPercentagesForApp/v0002/?gameid={appId.ToString(CultureInfo.InvariantCulture)}&format=json");
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AchievementRelay", "0.4.2"));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<string, double>(StringComparer.Ordinal);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return SteamRarityResponseParser.Parse(document.RootElement);
        }
        catch (Exception)
        {
            // Rarity is optional enrichment. No provider, transport, parsing,
            // disposal, or unexpected local failure may strand a proven and
            // durably pending achievement delivery.
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }
    }
}
