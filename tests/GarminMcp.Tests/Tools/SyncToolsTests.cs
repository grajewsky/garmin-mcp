using System.Text.Json;
using GarminMcp.Tools;
using GarminMcp.Tests.Support;

namespace GarminMcp.Tests.Tools;

[Collection("postgres")]
public class SyncToolsTests(PostgresFixture fx)
{
    private async Task<SyncTools> CreateAsync() => new((await SyncTestHost.CreateAsync(fx)).Runner);

    [Theory]
    [InlineData("2022-13-01", null)]
    [InlineData(null, "yesterday")]
    public async Task SyncTool_InvalidDate_ReturnsError(string? from, string? to)
    {
        var json = JsonDocument.Parse(await (await CreateAsync()).Sync(from, to));
        Assert.True(json.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task SyncTool_FromAfterTo_ReturnsError()
    {
        var json = JsonDocument.Parse(await (await CreateAsync()).Sync("2022-02-01", "2022-01-01"));
        Assert.True(json.RootElement.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task SyncStatus_NoJobs_NeverRun()
    {
        Assert.Equal("""{"status":"never_run"}""", await (await CreateAsync()).SyncStatus());
    }

    [Fact]
    public async Task SyncStatus_TimestampsUtcToTheSecond()
    {
        var host = await SyncTestHost.CreateAsync(fx);
        host.Handler.On("/activitylist-service", _ => TestGarmin.Json("[]"));
        await host.Runner.StartAsync(null, null);
        await host.Runner.CurrentTask!;

        var json = JsonDocument.Parse(await new SyncTools(host.Runner).SyncStatus()).RootElement;

        Assert.Equal("completed", json.GetProperty("status").GetString());
        foreach (var name in new[] { "started_at", "finished_at", "last_successful_sync_at" })
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$", json.GetProperty(name).GetString());
    }

    [Fact]
    public void ToolJson_DateTimeOffset_UtcToTheSecond()
    {
        var t = new DateTimeOffset(2026, 10, 23, 14, 5, 6, 789, TimeSpan.FromHours(2));
        Assert.Equal("""{"at":"2026-10-23T12:05:06Z","maybe":"2026-10-23T12:05:06Z"}""",
            ToolJson.Serialize(new { At = t, Maybe = (DateTimeOffset?)t }));
    }

    [Fact]
    public void ToolJson_UsesSnakeCase()
    {
        Assert.Equal("""{"items_done":1}""", ToolJson.Serialize(new { ItemsDone = 1 }));
    }
}
