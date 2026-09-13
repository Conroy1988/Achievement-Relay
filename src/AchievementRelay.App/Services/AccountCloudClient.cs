using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AchievementRelay.Core.Services;

namespace AchievementRelay.App.Services;

public sealed class AccountCloudClient : IDisposable
{
    public const string ProjectUrl = "https://vniujteastkitrmucebv.supabase.co";
    public const string ReturnUrl = "http://127.0.0.1:43821/auth/callback";
    private const string PublishableKey = "sb_publishable_VjCN4UiDA4Ku5JHSr30tiQ_FlyQEI9x";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AchievementRelay.AccountSession.v1");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { BaseAddress = new Uri(ProjectUrl), Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 2_100_000 };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private Session? _session;
    private CloudRow? _cachedRow;
    public Guid? UserId => _session?.UserId;
    public string AccountDisplayName => string.IsNullOrWhiteSpace(_session?.DisplayName) ? "Your Discord account" : _session.DisplayName;
    public bool HasRecoveryKey => _session?.Key is { Length: 32 };
    public bool IsConnected => _session is not null || File.Exists(_path);
    public sealed record CloudRow(Guid UserId, long Revision, string Ciphertext);
    private sealed record Session(Guid UserId, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt, byte[]? Key, string? DisplayName = null);

    public AccountCloudClient(AppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "account-session.bin");
        _http.DefaultRequestHeaders.Add("apikey", PublishableKey);
        try
        {
            if (File.Exists(_path))
            {
                var data = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
                try { _session = JsonSerializer.Deserialize<Session>(data, Json); }
                finally { CryptographicOperations.ZeroMemory(data); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { }
    }

    public async Task SignInAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var listener = new TcpListener(IPAddress.Loopback, 43821);
            listener.Start();
            try
            {
                var authorize = ProjectUrl + "/auth/v1/authorize?provider=discord&redirect_to=" + Uri.EscapeDataString(ReturnUrl)
                    + "&code_challenge=" + challenge + "&code_challenge_method=s256";
                Process.Start(new ProcessStartInfo(authorize) { UseShellExecute = true });
                while (true)
                {
                    using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
                    using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await using var stream = socket.GetStream();
                    var bytes = new List<byte>();
                    var buffer = new byte[1];
                    while (bytes.Count < 8192 && await stream.ReadAsync(buffer, requestTimeout.Token) > 0)
                    {
                        bytes.Add(buffer[0]);
                        if (bytes.Count >= 4 && bytes.TakeLast(4).SequenceEqual(new byte[] { 13, 10, 13, 10 })) break;
                    }
                    var firstLine = Encoding.ASCII.GetString(bytes.ToArray()).Split('\r')[0].Split(' ');
                    if (firstLine.Length != 3 || firstLine[0] != "GET" ||
                        !Uri.TryCreate(ReturnUrl[..ReturnUrl.IndexOf("/auth", StringComparison.Ordinal)] + firstLine[1], UriKind.Absolute, out var callback) ||
                        callback.AbsolutePath != "/auth/callback") continue;
                    var code = callback.Query.TrimStart('?').Split('&').Select(x => x.Split('=', 2))
                        .FirstOrDefault(x => x.Length == 2 && x[0] == "code")?[1];
                    if (string.IsNullOrEmpty(code)) continue;
                    using var response = await _http.PostAsJsonAsync("/auth/v1/token?grant_type=pkce",
                        new { auth_code = Uri.UnescapeDataString(code), code_verifier = verifier }, timeout.Token);
                    EnsureSuccess(response);
                    var token = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
                    var next = ParseSession(token, null);
                    var ownerPath = _path + ".owner";
                    if (File.Exists(ownerPath) && File.ReadAllText(ownerPath).Trim() != next.UserId.ToString("D"))
                        throw new InvalidOperationException("This Windows profile is paired with a different Relay account. Sign in with that account to keep account data separate.");
                    // Never transfer one account's recovery key to a different account.
                    if (_session?.UserId == next.UserId) next = next with { Key = _session.Key };
                    _session = next;
                    _cachedRow = null;
                    File.WriteAllText(ownerPath, next.UserId.ToString("D"));
                    Save();
                    var html = "<!doctype html><title>Achievement Relay</title><h1>Signed in</h1><p>Return to Achievement Relay. You can close this tab.</p>";
                    var body = Encoding.UTF8.GetBytes(html);
                    await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\nContent-Length: " + body.Length + "\r\n\r\n"), timeout.Token);
                    await stream.WriteAsync(body, timeout.Token);
                    return;
                }
            }
            finally { listener.Stop(); }
        }
        finally { _gate.Release(); }
    }

    public async Task<CloudRow?> ReadAsync(CancellationToken ct = default)
    {
        if (_cachedRow is not null && _cachedRow.UserId == UserId)
        {
            using var check = await SendAsync(HttpMethod.Get, "/rest/v1/relay_accounts?select=revision", null, ct);
            EnsureSuccess(check);
            var versions = await check.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (versions.GetArrayLength() == 1 && versions[0].GetProperty("revision").GetInt64() == _cachedRow.Revision) return _cachedRow;
        }
        using var response = await SendAsync(HttpMethod.Get, "/rest/v1/relay_accounts?select=user_id,revision,ciphertext", null, ct);
        EnsureSuccess(response);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (rows.GetArrayLength() == 0) return null;
        var row = rows[0];
        return _cachedRow = new CloudRow(row.GetProperty("user_id").GetGuid(), row.GetProperty("revision").GetInt64(), row.GetProperty("ciphertext").GetString()!);
    }

    public async Task<bool> WriteAsync(string ciphertext, long? revision, CancellationToken ct = default)
    {
        var id = UserId ?? throw new InvalidOperationException("Sign in first.");
        var path = "/rest/v1/relay_accounts" + (revision is null ? "" : "?user_id=eq." + id + "&revision=eq." + revision);
        using var response = await SendAsync(revision is null ? HttpMethod.Post : HttpMethod.Patch, path,
            new { user_id = id, revision = (revision ?? 0) + 1, ciphertext, updated_at = DateTimeOffset.UtcNow }, ct, true);
        if (response.StatusCode == HttpStatusCode.Conflict) return false;
        EnsureSuccess(response);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (rows.GetArrayLength() != 1) return false;
        _cachedRow = new CloudRow(id, (revision ?? 0) + 1, ciphertext);
        return true;
    }

    public async Task<string> CreateRecoveryKeyAsync()
    {
        if (await ReadAsync() is not null) throw new InvalidOperationException("Enter the recovery key from your existing device.");
        _session = (_session ?? throw new InvalidOperationException("Sign in first.")) with { Key = AccountCipher.NewKey() };
        Save();
        return ExportRecoveryKey();
    }
    public async Task SetRecoveryKeyAsync(string value)
    {
        var key = Convert.FromBase64String(value.Trim());
        if (key.Length != 32) throw new InvalidOperationException("The recovery key is not valid.");
        var row = await ReadAsync();
        if (row is not null)
        {
            var plaintext = AccountCipher.Decrypt(row.Ciphertext, key, row.UserId);
            CryptographicOperations.ZeroMemory(plaintext);
        }
        _session = (_session ?? throw new InvalidOperationException("Sign in first.")) with { Key = key };
        Save();
    }
    public string ExportRecoveryKey() => Convert.ToBase64String(_session?.Key ?? throw new InvalidOperationException("Unlock your account first."));
    public string Encrypt(byte[] data) => AccountCipher.Encrypt(data, _session?.Key ?? throw new InvalidOperationException("Unlock your account first."), UserId!.Value);
    public byte[] Decrypt(CloudRow row) => AccountCipher.Decrypt(row.Ciphertext, _session?.Key ?? throw new InvalidOperationException("Enter your recovery key."), row.UserId);

    public async Task<bool> ClaimAsync(string eventId, Uri destination, CancellationToken ct)
    {
        var hash = ClaimHash(eventId, destination);
        using var response = await SendAsync(HttpMethod.Post, "/rest/v1/rpc/relay_claim_delivery", new { event_hash = hash }, ct);
        EnsureSuccess(response);
        return await response.Content.ReadFromJsonAsync<bool>(ct);
    }
    public async Task ReleaseRejectedClaimAsync(string eventId, Uri destination, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Delete, "/rest/v1/relay_delivery_claims?event_hash=eq." + ClaimHash(eventId, destination), null, ct);
        EnsureSuccess(response);
    }
    private string ClaimHash(string eventId, Uri destination)
    {
        var key = _session?.Key ?? throw new InvalidOperationException("Unlock account sync before posting.");
        return Convert.ToHexStringLower(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(eventId + "\n" + destination.AbsoluteUri)));
    }
    public async Task SignOutAsync()
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Post, "/auth/v1/logout?scope=local", null, default);
            EnsureSuccess(response);
        }
        finally
        {
            if (_session?.Key is { } key) CryptographicOperations.ZeroMemory(key);
            _session = null;
            if (File.Exists(_path)) File.Delete(_path);
        }
    }
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct, bool representation = false)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var session = _session ?? throw new InvalidOperationException("Sign in with Discord first.");
            if (session.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(1))
            {
                using var refresh = await _http.PostAsJsonAsync("/auth/v1/token?grant_type=refresh_token", new { refresh_token = session.RefreshToken }, ct);
                EnsureSuccess(refresh);
                _session = session = ParseSession(await refresh.Content.ReadFromJsonAsync<JsonElement>(ct), session.Key);
                Save();
            }
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
            if (representation) request.Headers.Add("Prefer", "return=representation");
            if (body is not null) request.Content = JsonContent.Create(body);
            return await _http.SendAsync(request, ct);
        }
        finally { _gate.Release(); }
    }
    private static Session ParseSession(JsonElement data, byte[]? key)
    {
        var user = data.GetProperty("user");
        string? displayName = null;
        // User-editable metadata is display text only, never an authorization input.
        if (user.TryGetProperty("user_metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
            foreach (var field in new[] { "full_name", "name", "preferred_username", "user_name" })
                if (metadata.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())) {
                    displayName = new string(value.GetString()!.Where(c => !char.IsControl(c)).Take(80).ToArray()); break;
                }
        return new Session(user.GetProperty("id").GetGuid(), data.GetProperty("access_token").GetString()!,
            data.GetProperty("refresh_token").GetString()!, DateTimeOffset.UtcNow.AddSeconds(data.GetProperty("expires_in").GetInt32()), key, displayName);
    }
    private void Save()
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_session, Json);
        try
        {
            File.WriteAllBytes(_path + ".tmp", ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
            File.Move(_path + ".tmp", _path, true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Account service unavailable (HTTP " + (int)response.StatusCode + "). Local data is unchanged.");
    }
    public void Dispose() { _http.Dispose(); _gate.Dispose(); }
}
