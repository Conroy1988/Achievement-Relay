using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

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
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AchievementRelay", "0.3.0"));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new Dictionary<string, double>(StringComparer.Ordinal);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("achievementpercentages", out var root) ||
                !root.TryGetProperty("achievements", out var achievements) ||
                achievements.ValueKind != JsonValueKind.Array)
            {
                return new Dictionary<string, double>(StringComparer.Ordinal);
            }

            var result = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var item in achievements.EnumerateArray())
            {
                if (!item.TryGetProperty("name", out var nameValue) ||
                    nameValue.GetString() is not { Length: > 0 } name ||
                    !item.TryGetProperty("percent", out var percentValue) ||
                    !percentValue.TryGetDouble(out var percent) ||
                    double.IsNaN(percent) || percent is < 0 or > 100)
                {
                    continue;
                }

                result[name] = percent;
            }

            return result;
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }
    }
}
