using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed class DiscordWebhookClient : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<RelayResult> SendAsync(
        Uri webhookUri,
        string jsonPayload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webhookUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPayload);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, AddWaitParameter(webhookUri));
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AchievementRelay", "0.1.0"));
                request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return RelayResult.Ok("Discord accepted the achievement post.");
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2);
                    await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay, cancellationToken);
                    continue;
                }

                return RelayResult.Fail(
                    $"Discord rejected the request ({(int)response.StatusCode} {response.ReasonPhrase}).",
                    (int)response.StatusCode);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return RelayResult.Fail("Discord did not respond before the request timed out.");
            }
            catch (HttpRequestException exception)
            {
                return RelayResult.Fail($"Could not reach Discord: {exception.Message}");
            }
        }

        return RelayResult.Fail("Discord rate-limited the request. Achievement Relay will try again on the next event.");
    }

    public void Dispose() => _httpClient.Dispose();

    private static Uri AddWaitParameter(Uri webhookUri)
    {
        var builder = new UriBuilder(webhookUri);
        var query = builder.Query.TrimStart('?');
        if (!query.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Any(item => item.StartsWith("wait=", StringComparison.OrdinalIgnoreCase)))
        {
            builder.Query = string.IsNullOrWhiteSpace(query) ? "wait=true" : $"{query}&wait=true";
        }

        return builder.Uri;
    }
}
