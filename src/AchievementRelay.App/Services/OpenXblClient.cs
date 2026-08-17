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
    TimeSpan? RetryAfter = null,
    bool Deferred = false,
    bool AllowanceProtected = false);

public sealed record OpenXblAchievementsResult(
    bool Success,
    string Message,
    IReadOnlyList<AchievementEvent>? Achievements = null,
    int? StatusCode = null,
    TimeSpan? RetryAfter = null,
    bool Deferred = false,
    bool AllowanceProtected = false);

public sealed record OpenXblTitleProgressResult(
    bool Success,
    string Message,
    IReadOnlyList<XboxTitleProgress>? Titles = null,
    int? StatusCode = null,
    TimeSpan? RetryAfter = null,
    bool Deferred = false,
    bool AllowanceProtected = false);

public sealed class OpenXblClient : IDisposable
{
    private const string ProtectedAllowanceMessage =
        "OpenXBL request allowance is being protected. Achievement Relay will resume automatically after the hourly window resets.";
    private static readonly Uri BaseAddress = new("https://api.xbl.io/");
    private static readonly TimeSpan EndpointProbeRetryAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan OperationLimitRetryAfter = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan RateLimitFallbackRetryAfter = TimeSpan.FromHours(1);
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
        "api/v2/achievements/player/{xuid}/title/{titleId}",
        "api/v2/achievements/x360/{xuid}/title/{titleId}",
        "api/v2/achievements/player/{xuid}/{titleId}",
        "api/v2/achievements/title/{titleId}",
        "v2/achievements/player/{xuid}/title/{titleId}",
        "v2/achievements/x360/{xuid}/title/{titleId}",
        "v2/achievements/player/{xuid}/{titleId}",
        "v2/achievements/title/{titleId}"
    ];
    private const int MaximumResponseCharacters = 20 * 1024 * 1024;
    private const int MaximumContinuationPages = 20;
    private const int MaximumAccountRequestsPerOperation = 2;
    private const int MaximumTitleProgressRequestsPerOperation = 4;
    public const int MaximumTitleDetailRequestsPerOperation = 12;

    private string? _preferredAccountRoute;
    private string? _preferredTitleProgressRouteTemplate;
    private readonly Dictionary<string, string> _preferredTitleAchievementRouteTemplates =
        new(StringComparer.Ordinal);
    private readonly OpenXblRequestBudget _requestBudget = new();

    private readonly HttpClient _httpClient = new(new HttpClientHandler
    {
        // X-Authorization is a custom credential header. Never allow a
        // provider redirect to forward it to another origin.
        AllowAutoRedirect = false
    })
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

        var operationBudget = new RequestOperationBudget(MaximumAccountRequestsPerOperation);
        var startDecision = _requestBudget.CanStartOperation(
            OpenXblRequestPriority.Essential,
            operationBudget.MaximumRequests,
            DateTimeOffset.UtcNow);
        if (!startDecision.Allowed)
        {
            return new OpenXblAccountResult(
                false,
                ProtectedAllowanceMessage,
                RetryAfter: startDecision.RetryAfter,
                Deferred: true,
                AllowanceProtected: true);
        }

        ApiResponse? lastResponse = null;
        var receivedUnreadableResponse = false;
        foreach (var route in PreferRoute(_preferredAccountRoute, AccountRoutes))
        {
            var response = await SendAsync(
                route,
                normalized,
                OpenXblRequestPriority.Essential,
                operationBudget,
                cancellationToken);
            lastResponse = response;
            if (!response.Success || response.Content is null)
            {
                if (response.StatusCode != (int)HttpStatusCode.NotFound)
                {
                    return new OpenXblAccountResult(
                        false,
                        response.Message,
                        StatusCode: response.StatusCode,
                        RetryAfter: response.RetryAfter,
                        Deferred: response.Deferred,
                        AllowanceProtected: response.AllowanceProtected);
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
            RetryAfter: lastResponse?.RetryAfter ?? EndpointProbeRetryAfter,
            Deferred: lastResponse?.Deferred ?? false,
            AllowanceProtected: lastResponse?.AllowanceProtected ?? false);
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

        var operationBudget = new RequestOperationBudget(MaximumTitleProgressRequestsPerOperation);
        var startDecision = _requestBudget.CanStartOperation(
            OpenXblRequestPriority.Essential,
            operationBudget.MaximumRequests,
            DateTimeOffset.UtcNow);
        if (!startDecision.Allowed)
        {
            return new OpenXblTitleProgressResult(
                false,
                ProtectedAllowanceMessage,
                RetryAfter: startDecision.RetryAfter,
                Deferred: true,
                AllowanceProtected: true);
        }

        var escapedAccountId = Uri.EscapeDataString(accountId.Trim());
        ApiResponse? lastResponse = null;
        var receivedUnreadableResponse = false;
        foreach (var routeTemplate in PreferRoute(
                     _preferredTitleProgressRouteTemplate,
                     TitleProgressRouteTemplates))
        {
            var route = routeTemplate.Replace("{xuid}", escapedAccountId, StringComparison.Ordinal);
            var response = await SendAsync(
                route,
                normalized,
                OpenXblRequestPriority.Essential,
                operationBudget,
                cancellationToken);
            lastResponse = response;
            if (!response.Success || response.Content is null)
            {
                if (response.StatusCode != (int)HttpStatusCode.NotFound)
                {
                    return new OpenXblTitleProgressResult(
                        false,
                        response.Message,
                        StatusCode: response.StatusCode,
                        RetryAfter: response.RetryAfter,
                        Deferred: response.Deferred,
                        AllowanceProtected: response.AllowanceProtected);
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
            RetryAfter: lastResponse?.RetryAfter ?? EndpointProbeRetryAfter,
            Deferred: lastResponse?.Deferred ?? false,
            AllowanceProtected: lastResponse?.AllowanceProtected ?? false);
    }

    public async Task<OpenXblAchievementsResult> GetTitleAchievementsAsync(
        string apiKey,
        string accountId,
        string titleId,
        int expectedUnlockedCount,
        OpenXblRequestPriority priority = OpenXblRequestPriority.Essential,
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

        var operationBudget = new RequestOperationBudget(MaximumTitleDetailRequestsPerOperation);
        var startDecision = _requestBudget.CanStartOperation(
            priority,
            operationBudget.MaximumRequests,
            DateTimeOffset.UtcNow);
        if (!startDecision.Allowed)
        {
            return new OpenXblAchievementsResult(
                false,
                ProtectedAllowanceMessage,
                RetryAfter: startDecision.RetryAfter,
                Deferred: true,
                AllowanceProtected: true);
        }

        var escapedAccountId = Uri.EscapeDataString(accountId.Trim());
        var escapedTitleId = Uri.EscapeDataString(titleId.Trim());
        ApiResponse? lastResponse = null;
        var receivedUnreadableResponse = false;
        IReadOnlyList<AchievementEvent>? bestAchievements = null;
        var bestCountDistance = int.MaxValue;
        foreach (var routeTemplate in PreferTitleAchievementRoute(titleId))
        {
            var route = routeTemplate
                .Replace("{xuid}", escapedAccountId, StringComparison.Ordinal)
                .Replace("{titleId}", escapedTitleId, StringComparison.Ordinal);
            var response = await SendAsync(
                route,
                normalized,
                priority,
                operationBudget,
                cancellationToken);
            lastResponse = response;
            if (!response.Success || response.Content is null)
            {
                if (response.StatusCode != (int)HttpStatusCode.NotFound &&
                    response.StatusCode != (int)HttpStatusCode.BadRequest)
                {
                    return new OpenXblAchievementsResult(
                        false,
                        response.Message,
                        StatusCode: response.StatusCode,
                        RetryAfter: response.RetryAfter,
                        Deferred: response.Deferred,
                        AllowanceProtected: response.AllowanceProtected);
                }

                continue;
            }

            try
            {
                var achievementsById = OpenXblResponseParser
                    .ParseAchievements(response.Content, accountId, titleId)
                    .ToDictionary(achievement => achievement.Id, StringComparer.Ordinal);
                var continuationToken = OpenXblResponseParser.ParseContinuationToken(response.Content);
                var seenContinuationTokens = new HashSet<string>(StringComparer.Ordinal);
                var continuationPages = 0;
                var routePrefix = routeTemplate.StartsWith("v2/", StringComparison.Ordinal)
                    ? "v2/"
                    : "api/v2/";

                while (achievementsById.Count < Math.Max(0, expectedUnlockedCount) &&
                       !string.IsNullOrWhiteSpace(continuationToken) &&
                       seenContinuationTokens.Add(continuationToken) &&
                       continuationPages++ < MaximumContinuationPages)
                {
                    var continuationRoute = $"{routePrefix}achievements/title/{escapedTitleId}/{Uri.EscapeDataString(continuationToken)}";
                    var continuationResponse = await SendAsync(
                        continuationRoute,
                        normalized,
                        priority,
                        operationBudget,
                        cancellationToken);
                    lastResponse = continuationResponse;
                    if (!continuationResponse.Success || continuationResponse.Content is null)
                    {
                        if (continuationResponse.StatusCode != (int)HttpStatusCode.NotFound &&
                            continuationResponse.StatusCode != (int)HttpStatusCode.BadRequest)
                        {
                            return new OpenXblAchievementsResult(
                                false,
                                continuationResponse.Message,
                                StatusCode: continuationResponse.StatusCode,
                                RetryAfter: continuationResponse.RetryAfter,
                                Deferred: continuationResponse.Deferred,
                                AllowanceProtected: continuationResponse.AllowanceProtected);
                        }

                        break;
                    }

                    foreach (var achievement in OpenXblResponseParser.ParseAchievements(
                                 continuationResponse.Content,
                                 accountId,
                                 titleId))
                    {
                        achievementsById.TryAdd(achievement.Id, achievement);
                    }

                    continuationToken = OpenXblResponseParser.ParseContinuationToken(continuationResponse.Content);
                }

                var achievements = achievementsById.Values
                    .OrderBy(achievement => achievement.UnlockedAt ?? DateTimeOffset.MaxValue)
                    .ThenBy(achievement => achievement.Id, StringComparer.Ordinal)
                    .ToArray();
                var countDistance = Math.Abs(achievements.Length - Math.Max(0, expectedUnlockedCount));
                if (bestAchievements is null ||
                    countDistance < bestCountDistance ||
                    (countDistance == bestCountDistance && achievements.Length > bestAchievements.Count))
                {
                    bestAchievements = achievements;
                    bestCountDistance = countDistance;
                }

                if (achievements.Length == Math.Max(0, expectedUnlockedCount))
                {
                    _preferredTitleAchievementRouteTemplates[titleId] = routeTemplate;
                    return new OpenXblAchievementsResult(
                        true,
                        $"OpenXBL returned {achievements.Length} unlocked achievement{(achievements.Length == 1 ? string.Empty : "s")} for the changed title.",
                        achievements,
                        response.StatusCode);
                }
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

        if (bestAchievements is not null)
        {
            return new OpenXblAchievementsResult(
                true,
                $"OpenXBL returned {bestAchievements.Count} unlocked achievement{(bestAchievements.Count == 1 ? string.Empty : "s")}, but the title index reports {Math.Max(0, expectedUnlockedCount)}.",
                bestAchievements,
                lastResponse?.StatusCode,
                EndpointProbeRetryAfter);
        }

        var message = receivedUnreadableResponse
            ? "OpenXBL returned title achievement data that Achievement Relay could not read from its current account routes."
            : "OpenXBL did not expose a compatible achievement-detail route for this account. Achievement Relay will retry automatically.";
        return new OpenXblAchievementsResult(
            false,
            message,
            StatusCode: lastResponse?.StatusCode,
            RetryAfter: lastResponse?.RetryAfter ?? EndpointProbeRetryAfter,
            Deferred: lastResponse?.Deferred ?? false,
            AllowanceProtected: lastResponse?.AllowanceProtected ?? false);
    }

    public void Dispose() => _httpClient.Dispose();

    private IEnumerable<string> PreferTitleAchievementRoute(string titleId)
    {
        var preferredPrefix = _preferredTitleProgressRouteTemplate?.StartsWith("v2/", StringComparison.Ordinal) == true
            ? "v2/"
            : "api/v2/";
        var orderedRoutes = TitleAchievementRouteTemplates
            .OrderByDescending(route => route.StartsWith(preferredPrefix, StringComparison.Ordinal))
            .ToArray();
        _preferredTitleAchievementRouteTemplates.TryGetValue(titleId, out var preferredRoute);
        return PreferRoute(preferredRoute, orderedRoutes);
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
        OpenXblRequestPriority priority,
        RequestOperationBudget operationBudget,
        CancellationToken cancellationToken)
    {
        if (!operationBudget.HasCapacity)
        {
            return new ApiResponse(
                false,
                "This title needs additional OpenXBL pages. Achievement Relay paused it so one sync cannot drain the request allowance.",
                RetryAfter: OperationLimitRetryAfter,
                Deferred: true);
        }

        var requestDecision = _requestBudget.TryAcquire(priority, DateTimeOffset.UtcNow);
        if (!requestDecision.Allowed)
        {
            return new ApiResponse(
                false,
                ProtectedAllowanceMessage,
                RetryAfter: requestDecision.RetryAfter,
                Deferred: true,
                AllowanceProtected: true);
        }

        operationBudget.RecordRequest();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, relativePath);
            request.Headers.TryAddWithoutValidation("X-Authorization", apiKey);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("AchievementRelay", "0.4.2"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrWhiteSpace(CultureInfo.CurrentUICulture.Name))
            {
                request.Headers.TryAddWithoutValidation("Accept-Language", CultureInfo.CurrentUICulture.Name);
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                cancellationToken);

            var responseObservedAt = DateTimeOffset.UtcNow;
            var providerResetUtc = ObserveRateLimitHeaders(response, responseObservedAt);
            var statusCode = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                var retryAfter = GetRetryAfter(response.Headers.RetryAfter);
                if (response.StatusCode == HttpStatusCode.TooManyRequests && retryAfter is null)
                {
                    retryAfter = providerResetUtc is { } resetUtc && resetUtc > responseObservedAt
                        ? resetUtc - responseObservedAt
                        : RateLimitFallbackRetryAfter;
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    _requestBudget.ObserveRateLimited(responseObservedAt, retryAfter);
                }

                return new ApiResponse(
                    false,
                    MapError(response.StatusCode),
                    StatusCode: statusCode,
                    RetryAfter: retryAfter);
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

    private static TimeSpan? GetRetryAfter(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta)
        {
            return delta > TimeSpan.Zero ? delta : TimeSpan.FromSeconds(1);
        }

        if (retryAfter?.Date is { } date)
        {
            var delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1);
        }

        return null;
    }

    private DateTimeOffset? ObserveRateLimitHeaders(HttpResponseMessage response, DateTimeOffset observedAt)
    {
        var limit = GetIntegerHeader(response, "X-RateLimit-Limit", "RateLimit-Limit");
        var remaining = GetIntegerHeader(response, "X-RateLimit-Remaining", "RateLimit-Remaining");
        var resetUtc = GetResetHeader(
            response,
            observedAt,
            "X-RateLimit-Reset",
            "X-RateLimit-Reset-After",
            "RateLimit-Reset");
        _requestBudget.ObserveProviderWindow(limit, remaining, resetUtc, observedAt);
        return resetUtc;
    }

    private static int? GetIntegerHeader(HttpResponseMessage response, params string[] names)
    {
        var value = GetFirstHeaderValue(response, names);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
            ? parsed
            : null;
    }

    private static DateTimeOffset? GetResetHeader(
        HttpResponseMessage response,
        DateTimeOffset observedAt,
        params string[] names)
    {
        var value = GetFirstHeaderValue(response, names);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
        {
            try
            {
                if (numeric > 10_000_000_000)
                {
                    return DateTimeOffset.FromUnixTimeMilliseconds(numeric);
                }

                if (numeric > observedAt.ToUnixTimeSeconds() - 86_400)
                {
                    return DateTimeOffset.FromUnixTimeSeconds(numeric);
                }

                if (numeric > 0)
                {
                    return observedAt.AddSeconds(numeric);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? GetFirstHeaderValue(HttpResponseMessage response, params string[] names)
    {
        foreach (var name in names)
        {
            if (response.Headers.TryGetValues(name, out var values))
            {
                return values.FirstOrDefault();
            }
        }

        return null;
    }

    private sealed record ApiResponse(
        bool Success,
        string Message,
        string? Content = null,
        int? StatusCode = null,
        TimeSpan? RetryAfter = null,
        bool Deferred = false,
        bool AllowanceProtected = false);

    private sealed class RequestOperationBudget(int maximumRequests)
    {
        private int _requests;

        public int MaximumRequests { get; } = maximumRequests;

        public bool HasCapacity => _requests < MaximumRequests;

        public void RecordRequest() => _requests++;
    }
}
