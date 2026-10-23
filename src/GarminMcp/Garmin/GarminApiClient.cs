using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GarminMcp.Garmin;

/// <summary>
/// Klient danych Garmin: Bearer, odstęp między żądaniami, retry na 429/5xx, odświeżenie tokenu na 401 —
/// najwyżej raz na zadanie (<see cref="GarminAuthSession"/>). 401 na tokenie, który już zadziałał lub właśnie
/// został odświeżony, to błąd endpointu (<see cref="GarminApiException"/>), nie uwierzytelnienia.
/// </summary>
public sealed class GarminApiClient(
    HttpClient http, TokenProvider tokens, GarminAuthSession session, IOptions<GarminOptions> options,
    TimeProvider time, ILogger<GarminApiClient> log)
{
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

    public async Task<JsonDocument?> GetJsonAsync(string pathAndQuery, CancellationToken ct)
    {
        var bytes = await GetBytesAsync(pathAndQuery, ct);
        if (bytes is null || bytes.Length == 0) return null;
        return JsonDocument.Parse(bytes);
    }

    public async Task<byte[]?> GetBytesAsync(string pathAndQuery, CancellationToken ct)
    {
        var opts = options.Value;
        var retries = 0;
        while (true)
        {
            if (opts.RequestDelay > TimeSpan.Zero)
                await Task.Delay(opts.RequestDelay, ct);

            var (token, issued) = await tokens.AcquireAsync(ct);
            if (issued) session.NoteIssued();
            using var request = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, ct);
            var status = response.StatusCode;

            if (status == HttpStatusCode.NotFound || status == HttpStatusCode.NoContent)
                return null;
            if (response.IsSuccessStatusCode)
            {
                session.Confirm(token);
                return await response.Content.ReadAsByteArrayAsync(ct);
            }

            if (status == HttpStatusCode.Unauthorized)
            {
                if (!session.TryBeginRefresh(token))
                    throw new GarminApiException((int)status, pathAndQuery);
                // Po odświeżeniu kolejna 401 trafi w gałąź wyżej (odświeżenie zużyte) → GarminApiException.
                await tokens.RefreshAsync(token, ct);
                continue;
            }

            if ((status == HttpStatusCode.TooManyRequests || (int)status >= 500) && retries < opts.MaxRetries)
            {
                var delay = RetryDelay(response.Headers.RetryAfter, retries, opts.RetryBaseDelay, time.GetUtcNow());
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

    /// <summary>Opóźnienie przed ponowieniem: <c>Retry-After</c> (sekundy lub data HTTP, ≥ 0, maks. 60 s) albo backoff wykładniczy.</summary>
    internal static TimeSpan RetryDelay(RetryConditionHeaderValue? retryAfter, int retries, TimeSpan baseDelay, DateTimeOffset now)
    {
        TimeSpan? server = retryAfter?.Delta ?? (retryAfter?.Date is { } date ? date - now : null);
        if (server is { } d)
            return d < TimeSpan.Zero ? TimeSpan.Zero : d > MaxRetryAfter ? MaxRetryAfter : d;
        return baseDelay * Math.Pow(2, retries);
    }
}
