using System.Net;
using System.Net.Http.Headers;
using GarminMcp.Garmin;
using GarminMcp.Tests.Support;

namespace GarminMcp.Tests.Garmin;

public class GarminApiClientTests
{
    private const string Data = "/wellness-api";

    private static InMemoryTokenStore CachedStore() => new()
    {
        Current = new StoredToken(TestGarmin.Jwt(DateTimeOffset.UtcNow.AddHours(20)), DateTimeOffset.UtcNow.AddHours(20)),
    };

    [Fact]
    public async Task GetJson_SendsBearer()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        var jwt = TestGarmin.Jwt(DateTimeOffset.UtcNow.AddHours(2));
        h.On("/personalapitoken-service", _ => TestGarmin.Text(jwt));
        h.On(Data, _ => TestGarmin.Json("{\"a\":1}"));

        using var doc = await client.GetJsonAsync($"{Data}/x", CancellationToken.None);

        Assert.Equal(1, doc!.RootElement.GetProperty("a").GetInt32());
        Assert.Equal($"Bearer {jwt}", h.Calls.Last().Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task GetJson_401_RefreshesAndRetriesOnce()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h, CachedStore());
        var n = 0;
        h.On(Data, _ => ++n == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : TestGarmin.Json("{\"ok\":true}"));

        using var doc = await client.GetJsonAsync($"{Data}/x", CancellationToken.None);

        Assert.True(doc!.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(1, h.CountCalls("/personalapitoken-service"));
        Assert.Equal(2, h.CountCalls(Data));
    }

    [Fact]
    public async Task GetJson_401AfterRefresh_ThrowsApi401()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h, CachedStore());
        h.On(Data, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<GarminApiException>(() => client.GetJsonAsync($"{Data}/x", CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal(2, h.CountCalls(Data));
        Assert.Equal(1, h.CountCalls("/personalapitoken-service"));
    }

    [Fact]
    public async Task GetJson_401OnFreshlyIssuedToken_ThrowsApi401WithoutSecondIssuance()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        h.On(Data, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<GarminApiException>(() => client.GetJsonAsync($"{Data}/x", CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal(1, h.CountCalls(Data));
        Assert.Equal(1, h.CountCalls("/personalapitoken-service"));
    }

    [Fact]
    public async Task GetJson_401_AtMostOneRefreshPerSession()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h, CachedStore());
        h.On(Data, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<GarminApiException>(() => client.GetJsonAsync($"{Data}/{i}", CancellationToken.None));

        Assert.Equal(1, h.CountCalls("/personalapitoken-service"));
    }

    [Fact]
    public async Task GetJson_401OnTokenThatAlreadyWorked_ThrowsApi401WithoutRefresh()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        h.On(Data, _ => TestGarmin.Json("{}"));
        h.On($"{Data}/denied", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using (await client.GetJsonAsync($"{Data}/ok", CancellationToken.None)) { }
        var ex = await Assert.ThrowsAsync<GarminApiException>(() => client.GetJsonAsync($"{Data}/denied", CancellationToken.None));

        Assert.Equal(401, ex.StatusCode);
        Assert.Equal(1, h.CountCalls("/personalapitoken-service"));
    }

    [Fact]
    public async Task GetJson_401AndTokenEndpointRejects_ThrowsAuth_KeepsStoredToken()
    {
        var h = new FakeGarminHandler();
        var stored = new StoredToken(TestGarmin.Jwt(DateTimeOffset.UtcNow.AddHours(20)), DateTimeOffset.UtcNow.AddHours(20));
        var store = new InMemoryTokenStore { Current = stored };
        var (client, _) = TestGarmin.Create(h, store);
        h.On(Data, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        h.On("/personalapitoken-service", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<GarminAuthException>(() => client.GetJsonAsync($"{Data}/x", CancellationToken.None));

        Assert.Equal(stored, store.Current);
    }

    [Fact]
    public async Task GetJson_429ThenOk_Retries()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        var n = 0;
        h.On(Data, _ =>
        {
            if (++n > 1) return TestGarmin.Json("{}");
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return r;
        });

        using var doc = await client.GetJsonAsync($"{Data}/x", CancellationToken.None);

        Assert.NotNull(doc);
        Assert.Equal(2, h.CountCalls(Data));
    }

    [Fact]
    public async Task GetJson_500Always_ThrowsApiExceptionAfter4Attempts()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        h.On(Data, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<GarminApiException>(() => client.GetJsonAsync($"{Data}/x", CancellationToken.None));

        Assert.Equal(500, ex.StatusCode);
        Assert.Equal($"{Data}/x", ex.Path);
        Assert.Equal(1 + new GarminOptions().MaxRetries, h.CountCalls(Data));
    }

    [Fact]
    public async Task GetJson_404_ReturnsNull()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        h.On(Data, _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await client.GetJsonAsync($"{Data}/x", CancellationToken.None));
    }

    [Fact]
    public async Task GetBytes_404_ReturnsNull()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        h.On(Data, _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        Assert.Null(await client.GetBytesAsync($"{Data}/x", CancellationToken.None));
    }

    [Fact]
    public void RetryDelay_DeltaAboveCap_CappedAt60s() =>
        Assert.Equal(TimeSpan.FromSeconds(60),
            GarminApiClient.RetryDelay(new RetryConditionHeaderValue(TimeSpan.FromHours(1)), 0, TimeSpan.FromSeconds(2), DateTimeOffset.UtcNow));

    [Fact]
    public void RetryDelay_HttpDate_UsesDifferenceFromNow()
    {
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(30),
            GarminApiClient.RetryDelay(new RetryConditionHeaderValue(now.AddSeconds(30)), 0, TimeSpan.FromSeconds(2), now));
    }

    [Fact]
    public void RetryDelay_HttpDateInPast_Zero()
    {
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero,
            GarminApiClient.RetryDelay(new RetryConditionHeaderValue(now.AddSeconds(-30)), 0, TimeSpan.FromSeconds(2), now));
    }

    [Fact]
    public void RetryDelay_HttpDateFarAway_CappedAt60s()
    {
        var now = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(60),
            GarminApiClient.RetryDelay(new RetryConditionHeaderValue(now.AddDays(1)), 0, TimeSpan.FromSeconds(2), now));
    }

    [Fact]
    public void RetryDelay_NoHeader_ExponentialBackoff() =>
        Assert.Equal(TimeSpan.FromSeconds(8),
            GarminApiClient.RetryDelay(null, 2, TimeSpan.FromSeconds(2), DateTimeOffset.UtcNow));
}
