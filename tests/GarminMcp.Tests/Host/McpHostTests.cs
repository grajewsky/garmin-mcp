using System.Net;
using GarminMcp.Hosting;
using GarminMcp.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

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

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("ok", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SyncStatus_ThroughMcpClient_EmptyDb_NeverRun()
    {
        var http = _factory.CreateClient();
        await using var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http);
        await using var client = await McpClient.CreateAsync(transport);

        var result = await client.CallToolAsync("sync_status");

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("""{"status":"never_run"}""", text);
    }

    [Fact]
    public async Task Mcp_ForeignOrigin_403()
    {
        var res = await PostInitializeAsync(origin: "http://attacker.example");

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://localhost:8765")]
    [InlineData("http://127.0.0.1:8765")]
    public async Task Mcp_NoOriginOrLoopbackOrigin_Ok(string? origin)
    {
        var res = await PostInitializeAsync(origin);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task ForeignHostHeader_Rejected()
    {
        var http = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/healthz");
        req.Headers.Host = "attacker.example";

        var res = await http.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost", true)]
    [InlineData("http://localhost:3000", true)]
    [InlineData("http://127.0.0.1:8765", true)]
    [InlineData("https://localhost", false)]
    [InlineData("http://localhost.attacker.example", false)]
    [InlineData("http://attacker.example", false)]
    [InlineData("null", false)]
    [InlineData("http://127.0.0.2", false)]
    public void LoopbackOrigin_IsAllowed(string origin, bool expected) =>
        Assert.Equal(expected, LoopbackOrigin.IsAllowed(origin));

    private async Task<HttpResponseMessage> PostInitializeAsync(string? origin)
    {
        var http = _factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""",
                System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.ParseAdd("application/json");
        req.Headers.Accept.ParseAdd("text/event-stream");
        if (origin is not null) req.Headers.Add("Origin", origin);
        return await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
    }
}
