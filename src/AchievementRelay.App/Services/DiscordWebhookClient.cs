using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using AchievementRelay.Core.Models;

namespace AchievementRelay.App.Services;

public sealed class DiscordWebhookClient : IDisposable
{
    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        // A webhook token is embedded in the request URI and the JSON can
        // contain account activity. Discord delivery must stay on the
        // validated origin instead of following provider-controlled redirects.
        AllowAutoRedirect = false
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public Task<RelayResult> SendAsync(
        Uri webhookUri,
        string jsonPayload,
        CancellationToken cancellationToken) =>
        SendAsync(webhookUri, jsonPayload, null, null, null, cancellationToken);

    public async Task<RelayResult> SendAsync(
        Uri webhookUri,
        string jsonPayload,
        byte[]? attachment = null,
        string? attachmentFileName = null,
        string? attachmentContentType = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(webhookUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPayload);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, AddWaitParameter(webhookUri));
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AchievementRelay", "0.4.1"));
                if (attachment is { Length: > 0 } && !string.IsNullOrWhiteSpace(attachmentFileName))
                {
                    var multipart = new MultipartFormDataContent();
                    multipart.Add(new StringContent(jsonPayload, Encoding.UTF8, "application/json"), "payload_json");
                    var file = new ByteArrayContent(attachment);
                    file.Headers.ContentType = new MediaTypeHeaderValue(
                        string.IsNullOrWhiteSpace(attachmentContentType) ? "image/png" : attachmentContentType);
                    multipart.Add(file, "files[0]", attachmentFileName);
                    request.Content = multipart;
                }
                else
                {
                    request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                }

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return RelayResult.Ok("Discord accepted the achievement post.");
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt == 0)
                {
                    var delay = GetRetryAfter(response.Headers.RetryAfter) ?? TimeSpan.FromSeconds(2);
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
            catch (HttpRequestException)
            {
                // Exception text can include the request URI, whose final path
                // segment is the webhook secret. Keep diagnostics actionable
                // without ever returning or logging transport exception text.
                return RelayResult.Fail("Could not reach Discord. Check the internet connection and try again.");
            }
        }

        return RelayResult.Fail("Discord rate-limited the request. Achievement Relay will try again on the next event.");
    }

    public void Dispose() => _httpClient.Dispose();

    private static Uri AddWaitParameter(Uri webhookUri)
    {
        var builder = new UriBuilder(webhookUri);
        var queryParts = builder.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(item => !item.StartsWith("wait=", StringComparison.OrdinalIgnoreCase))
            .Append("wait=true");
        builder.Query = string.Join('&', queryParts);

        return builder.Uri;
    }

    private static TimeSpan? GetRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : TimeSpan.FromMilliseconds(250);
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(250);
        }

        return null;
    }
}
