using System.Net;
using GarminMcp.Garmin;
using GarminMcp.Tests.Support;

namespace GarminMcp.Tests.Garmin;

public class GarminApiClientTests
{
    private const string Data = "/wellness-api";

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
    public async Task GetJson_401_InvalidatesAndRetriesOnce()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        var n = 0;
        h.On(Data, _ => ++n == 1
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : TestGarmin.Json("{\"ok\":true}"));

        using var doc = await client.GetJsonAsync($"{Data}/x", CancellationToken.None);

        Assert.True(doc!.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(2, h.CountCalls("/personalapitoken-service"));
        Assert.Equal(2, h.CountCalls(Data));
    }

    [Fact]
    public async Task GetJson_401Twice_ThrowsAuth()
    {
        var h = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(h);
        h.On(Data, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<GarminAuthException>(() => client.GetJsonAsync($"{Data}/x", CancellationToken.None));

        Assert.Equal("Garmin rejected a fresh access token (401).", ex.Message);
        Assert.Equal(2, h.CountCalls(Data));
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
}
