using GarminMcp.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;

namespace GarminMcp.Tests.Host;

[Collection("postgres")]
public sealed class McpHostTests(PostgresFixture fx) : IAsyncLifetime
{
    private static readonly string[] ExpectedTools =
    [
        "list_activities", "get_activity", "get_activity_timeseries", "get_sleep", "get_hrv", "get_body_battery",
        "get_stress", "get_daily_summary", "get_weight", "get_heart_rate_zones", "get_profile", "sync", "sync_status",
    ];

    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await using var db = await fx.CreateContextAsync();
        var cs = db.Database.GetConnectionString()!;
        var keyFile = TestGarmin.WriteKeyFile();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Garmin"] = cs,
                ["Garmin:ApiKeyFile"] = keyFile,
            })));
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task ListsAllTools()
    {
        var http = _factory.CreateClient();
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http);
        await using var client = await McpClient.CreateAsync(transport);

        var tools = await client.ListToolsAsync();

        Assert.Equal(ExpectedTools.Order(), tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task Healthz_Returns200()
    {
        var res = await _factory.CreateClient().GetAsync("/healthz");

        Assert.Equal(System.Net.HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("ok", await res.Content.ReadAsStringAsync());
    }
}
