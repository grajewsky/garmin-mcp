using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GarminMcp.Garmin;

/// <summary>
/// Wydaje i cache'uje token dostępowy Garmin. Nowy token tylko gdy brak, wygasł lub endpoint danych go odrzucił (<see cref="RefreshAsync"/>).
/// </summary>
public sealed class TokenProvider(HttpClient http, ITokenStore store, IOptions<GarminOptions> options, TimeProvider time)
{
    private const string TokenPath = "/personalapitoken-service/personal-api/personal-api-key/token";
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> GetTokenAsync(CancellationToken ct) => (await AcquireAsync(ct)).Token;

    /// <summary>Jak <see cref="GetTokenAsync"/>; <c>Issued</c> = token właśnie wydany przez Garmina (nie z cache).</summary>
    public async Task<(string Token, bool Issued)> AcquireAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var stored = await store.LoadAsync(ct);
            if (stored is not null && stored.ExpiresAt - ExpiryMargin > time.GetUtcNow())
                return (stored.Token, false);

            var fresh = await RequestTokenAsync(ct);
            await store.SaveAsync(fresh, ct);
            return (fresh.Token, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Wymusza nowy token po odrzuceniu <paramref name="rejectedToken"/>. Najpierw pobiera nowy token, a zapisany
    /// zastępuje dopiero po udanym wydaniu (przy błędzie stary zostaje). Jeśli zapisany token jest już inny niż
    /// odrzucony (wymieniony przez równoległe żądanie) i ważny, zwraca go bez wydawania nowego.
    /// </summary>
    public async Task<string> RefreshAsync(string rejectedToken, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var stored = await store.LoadAsync(ct);
            if (stored is not null && stored.Token != rejectedToken && stored.ExpiresAt - ExpiryMargin > time.GetUtcNow())
                return stored.Token;

            var fresh = await RequestTokenAsync(ct);
            await store.SaveAsync(fresh, ct);
            return fresh.Token;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StoredToken> RequestTokenAsync(CancellationToken ct)
    {
        var path = options.Value.ApiKeyFile;
        if (!File.Exists(path))
            throw new GarminAuthException($"Garmin API key file not found at {path}.");
        var key = (await File.ReadAllTextAsync(path, ct)).Trim();

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", key);
        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new GarminAuthException(
                "Garmin rejected the API key when issuing a token (401). The key may be invalid or Garmin may be limiting token requests — try again later or create a new key in Garmin Connect.");
        if (!response.IsSuccessStatusCode)
            throw new GarminAuthException(
                $"Garmin token endpoint failed with status {(int)response.StatusCode}; could not obtain an access token — try again later.");

        var jwt = (await response.Content.ReadAsStringAsync(ct)).Trim();
        if (!LooksLikeJwt(jwt))
            throw new GarminAuthException("Garmin token endpoint returned a response that is not a JWT; no token was stored.");
        return new StoredToken(jwt, ReadExpiry(jwt) ?? time.GetUtcNow().AddHours(24));
    }

    private static bool LooksLikeJwt(string s)
    {
        var parts = s.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0 && !p.Any(char.IsWhiteSpace));
    }

    private static DateTimeOffset? ReadExpiry(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2) return null;
            var b64 = parts[1].Replace('-', '+').Replace('_', '/');
            b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("exp", out var exp)
                && exp.TryGetInt64(out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (Exception e) when (e is FormatException or JsonException or ArgumentOutOfRangeException)
        {
        }
        return null;
    }
}
