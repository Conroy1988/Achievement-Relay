using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using AchievementRelay.Core.Models;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed record OpenXblAccountResult(
    bool Success,
    string Message,
    XboxAccount? Account = null,
    int? StatusCode = null,
    TimeSpan? RetryAfter = null);

public sealed record OpenXblAchievementsResult(
    bool Success,
    string Message,
    IReadOnlyList<AchievementEvent>? Achievements = null,
    int? StatusCode = null,
    TimeSpan? RetryAfter = null);

public sealed record OpenXblTitleProgressResult(
    bool Success,
    string Message,
    IReadOnlyList<XboxTitleProgress>? Titles = null,
    int? StatusCode = null,
    TimeSpan? RetryAfter = null);

public sealed class OpenXblClient : IDisposable
{
    private static readonly Uri BaseAddress = new("https://api.xbl.io/v2/");
    private const int MaximumResponseCharacters = 20 * 1024 * 1024;

    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = BaseAddress,
        Timeout = TimeSpan.FromSeconds(20),
        MaxResponseContentBufferSize = MaximumResponseCharacters
    };

    public async Task<OpenXblAccountResult> GetAccountAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        if (!OpenXblApiKeyValidator.TryNormalize(apiKey, out var normalized, out var error))
        {
            return new OpenXblAccountResult(false, error ?? "The OpenXBL API key is invalid.");
        }

        var response = await SendAsync("account", normalized, cancellationToken);
        if (!response.Success || response.Content is null)
        {
            return new OpenXblAccountResult(
                false,
                response.Message,
                StatusCode: response.StatusCode,
                RetryAfter: response.RetryAfter);
        }

        try
        {
            return new OpenXblAccountResult(
                true,
                "OpenXBL connected to the Xbox account.",
                OpenXblResponseParser.ParseAccount(response.Content),
                response.StatusCode);
        }
        catch (JsonException exception)
        {
            var message = exception.Message.StartsWith("OpenXBL ", StringComparison.Ordinal)
                ? exception.Message
                : "OpenXBL returned an account response that Achievement Relay could not read.";
            return new OpenXblAccountResult(false, message);
        }
    }

    public async Task<OpenXblTitleProgressResult> GetTitleProgressAsync(
        string apiKey,
        string accountId,
        CancellationToken cancellationToken = default)
    {
        if (!OpenXblApiKeyValidator.TryNormalize(apiKey, out var normalized, out var error))
        {
            return new OpenXblTitleProgressResult(false, error ?? "The OpenXBL API key is invalid.");
        }

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return new OpenXblTitleProgressResult(false, "The connected Xbox account identifier is missing.");
        }

        var response = await SendAsync(
            $"player/titleHistory/{Uri.EscapeDataString(accountId.Trim())}",
            normalized,
            cancellationToken);
        if (!response.Success || response.Content is null)
        {
            return new OpenXblTitleProgressResult(
                false,
                response.Message,
                StatusCode: response.StatusCode,
                RetryAfter: response.RetryAfter);
        }

        try
        {
            var titles = OpenXblResponseParser.ParseTitleProgress(response.Content);
            return new OpenXblTitleProgressResult(
                true,
                $"OpenXBL returned progress for {titles.Count} Xbox title{(titles.Count == 1 ? string.Empty : "s")}.",
                titles,
                response.StatusCode);
        }
        catch (JsonException)
        {
            return new OpenXblTitleProgressResult(false, "OpenXBL returned title progress that Achievement Relay could not read.");
        }
    }

    public async Task<OpenXblAchievementsResult> GetTitleAchievementsAsync(
        string apiKey,
        string accountId,
        string titleId,
        CancellationToken cancellationToken = default)
    {
        if (!OpenXblApiKeyValidator.TryNormalize(apiKey, out var normalized, out var error))
        {
            return new OpenXblAchievementsResult(false, error ?? "The OpenXBL API key is invalid.");
        }

        if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(titleId))
        {
            return new OpenXblAchievementsResult(false, "The Xbox account or title identifier is missing.");
        }

        var response = await SendAsync(
            $"achievements/player/{Uri.EscapeDataString(accountId.Trim())}/{Uri.EscapeDataString(titleId.Trim())}",
            normalized,
            cancellationToken);
        if (!response.Success || response.Content is null)
        {
            return new OpenXblAchievementsResult(
                false,
                response.Message,
                StatusCode: response.StatusCode,
                RetryAfter: response.RetryAfter);
        }

        try
        {
            var achievements = OpenXblResponseParser.ParseAchievements(response.Content, accountId, titleId);
            return new OpenXblAchievementsResult(
                true,
                $"OpenXBL returned {achievements.Count} unlocked achievement{(achievements.Count == 1 ? string.Empty : "s")} for the changed title.",
                achievements,
                response.StatusCode);
        }
        catch (JsonException)
        {
            return new OpenXblAchievementsResult(false, "OpenXBL returned title achievement data that Achievement Relay could not read.");
        }
    }

    public void Dispose() => _httpClient.Dispose();

    private async Task<ApiResponse> SendAsync(
        string relativePath,
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            request.Headers.TryAddWithoutValidation("X-Authorization", apiKey);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AchievementRelay", "0.2.1"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name))
            {
                request.Headers.TryAddWithoutValidation("Accept-Language", CultureInfo.CurrentUICulture.Name);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            var statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                return new ApiResponse(
                    false,
                    MapError(response.StatusCode),
                    StatusCode: statusCode,
                    RetryAfter: response.Headers.RetryAfter?.Delta);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            if (content.Length > MaximumResponseCharacters)
            {
                return new ApiResponse(false, "OpenXBL returned more achievement data than the app can safely process.", StatusCode: statusCode);
            }

            return new ApiResponse(true, "OpenXBL request succeeded.", content, statusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApiResponse(false, "OpenXBL did not respond before the request timed out.");
        }
        catch (HttpRequestException)
        {
            return new ApiResponse(false, "Achievement Relay could not reach OpenXBL. Check the internet connection and try again.");
        }
    }

    private static string MapError(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
            "OpenXBL rejected the API key. Create or copy a current key from your OpenXBL profile.",
        HttpStatusCode.TooManyRequests =>
            "OpenXBL rate-limited the account. Achievement Relay will try again automatically.",
        HttpStatusCode.NotFound =>
            "The OpenXBL achievement service was not found. Check OpenXBL's service status.",
        _ => $"OpenXBL rejected the request ({(int)statusCode} {statusCode})."
    };

    private sealed record ApiResponse(
        bool Success,
        string Message,
        string? Content = null,
        int? StatusCode = null,
        TimeSpan? RetryAfter = null);
}
