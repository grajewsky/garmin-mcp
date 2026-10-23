using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace GarminMcp.Garmin;

/// <summary>
/// Wydaje i cache'uje token dostępowy Garmin. Nowy token tylko gdy brak, wygasł lub został unieważniony.
/// </summary>
public sealed class TokenProvider(HttpClient http, ITokenStore store, IOptions<GarminOptions> options, TimeProvider time)
{
    private const string TokenPath = "/personalapitoken-service/personal-api/personal-api-key/token";
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var stored = await store.LoadAsync(ct);
            if (stored is not null && stored.ExpiresAt - ExpiryMargin > time.GetUtcNow())
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

    public Task InvalidateAsync(CancellationToken ct) => InvalidateAsync(null, ct);

    /// <summary>
    /// Czyści zapisany token. Gdy podano <paramref name="rejectedToken"/>, czyści tylko jeśli zapisany token
    /// nadal jest tym odrzuconym (nie kasuje tokenu już wymienionego przez równoległe żądanie).
    /// </summary>
    public async Task InvalidateAsync(string? rejectedToken, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (rejectedToken is not null)
            {
                var stored = await store.LoadAsync(ct);
                if (stored is null || stored.Token != rejectedToken)
                    return;
            }
            await store.ClearAsync(ct);
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
            throw new GarminApiException((int)response.StatusCode, TokenPath);

        var jwt = (await response.Content.ReadAsStringAsync(ct)).Trim();
        return new StoredToken(jwt, ReadExpiry(jwt) ?? time.GetUtcNow().AddHours(24));
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
