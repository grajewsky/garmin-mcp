using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GarminMcp.Garmin;

/// <summary>Klient danych Garmin: Bearer, odstęp między żądaniami, odświeżenie tokenu na 401, retry na 429/5xx.</summary>
public sealed class GarminApiClient(
    HttpClient http, TokenProvider tokens, IOptions<GarminOptions> options, ILogger<GarminApiClient> log)
{
    public async Task<JsonDocument?> GetJsonAsync(string pathAndQuery, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(pathAndQuery, ct);
        if (bytes is null || bytes.Length == 0) return null;
        return JsonDocument.Parse(bytes);
    }

    public async Task<byte[]?> GetBytesAsync(string pathAndQuery, CancellationToken ct)
    {
        var opts = options.Value;
        var refreshed = false;
        var retries = 0;
        while (true)
        {
            if (opts.RequestDelay > TimeSpan.Zero)
                await Task.Delay(opts.RequestDelay, ct);

            var token = await tokens.GetTokenAsync(ct);
            using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            var status = response.StatusCode;

            if (status == HttpStatusCode.NotFound || status == HttpStatusCode.NoContent)
                return null;
            if (response.IsSuccessStatusCode)
                return await response.Content.ReadAsByteArrayAsync(ct);

            if (status == HttpStatusCode.Unauthorized)
            {
                if (refreshed)
                    throw new GarminAuthException("Garmin rejected a fresh access token (401).");
                refreshed = true;
                await tokens.InvalidateAsync(ct);
                continue;
            }

            if ((status == HttpStatusCode.TooManyRequests || (int)status >= 500) && retries < opts.MaxRetries)
            {
                var delay = response.Headers.RetryAfter?.Delta
                            ?? opts.RetryBaseDelay * Math.Pow(2, retries);
                retries++;
                log.LogWarning("Garmin returned {Status} for {Path}; retry {Retry}/{Max} in {Delay}.",
                    (int)status, pathAndQuery, retries, opts.MaxRetries, delay);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, ct);
                continue;
            }

            throw new GarminApiException((int)status, pathAndQuery);
        }
    }
}
