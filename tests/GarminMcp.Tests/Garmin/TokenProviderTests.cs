using System.Net;
using GarminMcp.Garmin;
using GarminMcp.Tests.Support;
using Microsoft.Extensions.Options;

namespace GarminMcp.Tests.Garmin;

public class TokenProviderTests
{
    [Fact]
    public async Task GetToken_SecondCall_UsesCache()
    {
        var h = new FakeGarminHandler();
        var (_, tokens) = TestGarmin.Create(h);

        var a = await tokens.GetTokenAsync(CancellationToken.None);
        var b = await tokens.GetTokenAsync(CancellationToken.None);

        Assert.Equal(a, b);
        Assert.Equal(1, h.CountCalls("/personalapitoken-service"));
    }

    [Fact]
    public async Task GetToken_SendsApiKeyHeader_Trimmed()
    {
        var h = new FakeGarminHandler();
        var (_, tokens) = TestGarmin.Create(h);

        await tokens.GetTokenAsync(CancellationToken.None);

        Assert.Equal("ApiKey test-key", h.Calls[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task GetToken_StoresJwtExp()
    {
        var exp = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var h = new FakeGarminHandler();
        var store = new InMemoryTokenStore();
        var (_, tokens) = TestGarmin.Create(h, store);
        h.On("/personalapitoken-service", _ => TestGarmin.Text(TestGarmin.Jwt(exp)));

        await tokens.GetTokenAsync(CancellationToken.None);

        var saved = await store.LoadAsync(CancellationToken.None);
        Assert.Equal(exp, saved!.ExpiresAt);
    }

    [Fact]
    public async Task GetToken_ExpiringWithin5Min_FetchesNew()
    {
        var h = new FakeGarminHandler();
        var store = new InMemoryTokenStore
        {
            Current = new StoredToken("old", DateTimeOffset.UtcNow.AddMinutes(4)),
        };
        var (_, tokens) = TestGarmin.Create(h, store);

        var token = await tokens.GetTokenAsync(CancellationToken.None);

        Assert.NotEqual("old", token);
        Assert.Equal(1, h.CountCalls("/personalapitoken-service"));
    }

    [Fact]
    public async Task GetToken_401_ThrowsAuthWithoutKeyInMessage()
    {
        var h = new FakeGarminHandler();
        var (_, tokens) = TestGarmin.Create(h);
        h.On("/personalapitoken-service", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var ex = await Assert.ThrowsAsync<GarminAuthException>(() => tokens.GetTokenAsync(CancellationToken.None));

        Assert.Contains("401", ex.Message);
        Assert.DoesNotContain("test-key", ex.Message);
    }

    [Fact]
    public async Task GetToken_MissingKeyFile_ThrowsAuth()
    {
        var h = new FakeGarminHandler();
        var (_, tokens) = TestGarmin.Create(h, keyFile: "/nonexistent/garmin_api_key");

        var ex = await Assert.ThrowsAsync<GarminAuthException>(() => tokens.GetTokenAsync(CancellationToken.None));

        Assert.Contains("not found", ex.Message);
    }

    [Fact]
    public async Task Refresh_StaleRejectedToken_ReturnsCurrentWithoutIssuing()
    {
        var h = new FakeGarminHandler();
        var store = new InMemoryTokenStore();
        var (_, tokens) = TestGarmin.Create(h, store);
        var current = await tokens.GetTokenAsync(CancellationToken.None);

        var refreshed = await tokens.RefreshAsync("already-replaced-token", CancellationToken.None);

        Assert.Equal(current, refreshed);
        Assert.Equal(current, (await store.LoadAsync(CancellationToken.None))!.Token);
        Assert.Equal(1, h.CountCalls("/personalapitoken-service"));
    }

    [Fact]
    public async Task Refresh_MatchingRejectedToken_ReplacesStoredToken()
    {
        var h = new FakeGarminHandler();
        var store = new InMemoryTokenStore();
        var (_, tokens) = TestGarmin.Create(h, store);
        var current = await tokens.GetTokenAsync(CancellationToken.None);
        var next = TestGarmin.Jwt(DateTimeOffset.UtcNow.AddHours(23));
        h.On("/personalapitoken-service", _ => TestGarmin.Text(next));

        var refreshed = await tokens.RefreshAsync(current, CancellationToken.None);

        Assert.Equal(next, refreshed);
        Assert.Equal(next, (await store.LoadAsync(CancellationToken.None))!.Token);
    }

    [Fact]
    public async Task Refresh_IssuanceFails_KeepsStoredToken()
    {
        var h = new FakeGarminHandler();
        var store = new InMemoryTokenStore();
        var (_, tokens) = TestGarmin.Create(h, store);
        var current = await tokens.GetTokenAsync(CancellationToken.None);
        h.On("/personalapitoken-service", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<GarminAuthException>(() => tokens.RefreshAsync(current, CancellationToken.None));

        Assert.Equal(current, (await store.LoadAsync(CancellationToken.None))!.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html>oops</html>")]
    [InlineData("a.b")]
    [InlineData("a..c")]
    public async Task GetToken_NonJwtBody_ThrowsAuth_StoresNothing(string body)
    {
        var h = new FakeGarminHandler();
        var store = new InMemoryTokenStore();
        var (_, tokens) = TestGarmin.Create(h, store);
        h.On("/personalapitoken-service", _ => TestGarmin.Text(body));

        var ex = await Assert.ThrowsAsync<GarminAuthException>(() => tokens.GetTokenAsync(CancellationToken.None));

        Assert.DoesNotContain("oops", ex.Message);
        Assert.Null(store.Current);
    }

    [Fact]
    public async Task GetToken_TokenEndpoint500_ThrowsAuth()
    {
        var h = new FakeGarminHandler();
        var (_, tokens) = TestGarmin.Create(h);
        h.On("/personalapitoken-service", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Assert.ThrowsAsync<GarminAuthException>(() => tokens.GetTokenAsync(CancellationToken.None));

        Assert.Contains("500", ex.Message);
    }
}
