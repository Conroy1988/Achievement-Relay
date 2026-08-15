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
    private static readonly Uri BaseAddress = new("https://api.xbl.io/");
    private static readonly TimeSpan EndpointProbeRetryAfter = TimeSpan.FromMinutes(5);
    private static readonly string[] AccountRoutes =
    [
        "api/v2/account",
        "v2/account"
    ];
    private static readonly string[] TitleProgressRouteTemplates =
    [
        "api/v2/player/titleHistory",
        "v2/player/titleHistory",
        "api/v2/achievements/player/{xuid}",
        "v2/achievements/player/{xuid}"
    ];
    private static readonly string[] TitleAchievementRouteTemplates =
    [
        "api/v2/achievements/player/{xuid}/{titleId}",
        "api/v2/achievements/title/{titleId}",
        "v2/achievements/player/{xuid}/{titleId}",
        "v2/achievements/title/{titleId}"
    ];
    private const int MaximumResponseCharacters = 20 * 1024 * 1024;

    private string? _preferredAccountRoute;
    private string? _preferredTitleProgressRouteTemplate;
    private string? _preferredTitleAchievementRouteTemplate;

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

        ApiResponse? lastResponse = null;
        var receivedUnreadableResponse = false;
        foreach (var route in PreferRoute(_preferredAccountRoute, AccountRoutes))
        {
            var response = await SendAsync(route, normalized, cancellationToken);
            lastResponse = response;
            if (!response.Success || response.Content is null)
            {
                if (response.StatusCode != (int)HttpStatusCode.NotFound)
                {
                    return new OpenXblAccountResult(
                        false,
                        response.Message,
                        StatusCode: response.StatusCode,
                        RetryAfter: response.RetryAfter);
                }

                continue;
            }

            try
            {
                var account = OpenXblResponseParser.ParseAccount(response.Content);
                _preferredAccountRoute = route;
                return new OpenXblAccountResult(
                    true,
                    "OpenXBL connected to the Xbox account.",
                    account,
                    response.StatusCode);
            }
            catch (JsonException)
            {
                receivedUnreadableResponse = true;
            }
            catch (ArgumentException)
            {
                receivedUnreadableResponse = true;
            }
        }

        var message = receivedUnreadableResponse
            ? "OpenXBL accepted the API key, but did not return a usable Xbox profile. Confirm the intended Xbox profile is connected in OpenXBL, then try again."
            : lastResponse?.Message ?? "Achievement Relay could not find OpenXBL's account service.";
        return new OpenXblAccountResult(
            false,
            message,
            StatusCode: lastResponse?.StatusCode,
            RetryAfter: lastResponse?.RetryAfter ?? EndpointProbeRetryAfter);
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

        var escapedAccountId = Uri.EscapeDataString(accountId.Trim());
        ApiResponse? lastResponse = null;
        var receivedUnreadableResponse = false;
        foreach (var routeTemplate in PreferRoute(
                     _preferredTitleProgressRouteTemplate,
                     TitleProgressRouteTemplates))
        {
            var route = routeTemplate.Replace("{xuid}", escapedAccountId, StringComparison.Ordinal);
            var response = await SendAsync(route, normalized, cancellationToken);
            lastResponse = response;
            if (!response.Success || response.Content is null)
            {
                if (response.StatusCode != (int)HttpStatusCode.NotFound)
                {
                    return new OpenXblTitleProgressResult(
                        false,
                        response.Message,
                        StatusCode: response.StatusCode,
                        RetryAfter: response.RetryAfter);
                }

                continue;
            }

            try
            {
                var titles = OpenXblResponseParser.ParseTitleProgress(response.Content);
                _preferredTitleProgressRouteTemplate = routeTemplate;
                return new OpenXblTitleProgressResult(
                    true,
                    $"OpenXBL returned progress for {titles.Count} Xbox title{(titles.Count == 1 ? string.Empty : "s")}.",
                    titles,
                    response.StatusCode);
            }
            catch (JsonException)
            {
                receivedUnreadableResponse = true;
            }
            catch (ArgumentException)
            {
                receivedUnreadableResponse = true;
            }
        }

        var message = receivedUnreadableResponse
            ? "OpenXBL returned title data in a format that Achievement Relay could not read from its current account routes."
            : "OpenXBL did not expose a compatible title-progress route for this account. Achievement Relay will retry automatically.";
        return new OpenXblTitleProgressResult(
            false,
            message,
            StatusCode: lastResponse?.StatusCode,
            RetryAfter: lastResponse?.RetryAfter ?? EndpointProbeRetryAfter);
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

        var escapedAccountId = Uri.EscapeDataString(accountId.Trim());
        var escapedTitleId = Uri.EscapeDataString(titleId.Trim());
        ApiResponse? lastResponse = null;
        var receivedUnreadableResponse = false;
        foreach (var routeTemplate in PreferTitleAchievementRoute())
        {
            var route = routeTemplate
                .Replace("{xuid}", escapedAccountId, StringComparison.Ordinal)
                .Replace("{titleId}", escapedTitleId, StringComparison.Ordinal);
            var response = await SendAsync(route, normalized, cancellationToken);
            lastResponse = response;
            if (!response.Success || response.Content is null)
            {
                if (response.StatusCode != (int)HttpStatusCode.NotFound)
                {
                    return new OpenXblAchievementsResult(
                        false,
                        response.Message,
                        StatusCode: response.StatusCode,
                        RetryAfter: response.RetryAfter);
                }

                continue;
            }

            try
            {
                var achievements = OpenXblResponseParser.ParseAchievements(response.Content, accountId, titleId);
                _preferredTitleAchievementRouteTemplate = routeTemplate;
                return new OpenXblAchievementsResult(
                    true,
                    $"OpenXBL returned {achievements.Count} unlocked achievement{(achievements.Count == 1 ? string.Empty : "s")} for the changed title.",
                    achievements,
                    response.StatusCode);
            }
            catch (JsonException)
            {
                receivedUnreadableResponse = true;
            }
            catch (ArgumentException)
            {
                receivedUnreadableResponse = true;
            }
        }

        var message = receivedUnreadableResponse
            ? "OpenXBL returned title achievement data that Achievement Relay could not read from its current account routes."
            : "OpenXBL did not expose a compatible achievement-detail route for this account. Achievement Relay will retry automatically.";
        return new OpenXblAchievementsResult(
            false,
            message,
            StatusCode: lastResponse?.StatusCode,
            RetryAfter: lastResponse?.RetryAfter ?? EndpointProbeRetryAfter);
    }

    public void Dispose() => _httpClient.Dispose();

    private IEnumerable<string> PreferTitleAchievementRoute()
    {
        var preferredPrefix = _preferredTitleProgressRouteTemplate?.StartsWith("v2/", StringComparison.Ordinal) == true
            ? "v2/"
            : "api/v2/";
        var orderedRoutes = TitleAchievementRouteTemplates
            .OrderByDescending(route => route.StartsWith(preferredPrefix, StringComparison.Ordinal))
            .ToArray();
        return PreferRoute(_preferredTitleAchievementRouteTemplate, orderedRoutes);
    }

    private static IEnumerable<string> PreferRoute(string? preferredRoute, IEnumerable<string> routes)
    {
        if (!string.IsNullOrWhiteSpace(preferredRoute))
        {
            yield return preferredRoute;
        }

        foreach (var route in routes)
        {
            if (!string.Equals(route, preferredRoute, StringComparison.Ordinal))
            {
                yield return route;
            }
        }
    }

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
