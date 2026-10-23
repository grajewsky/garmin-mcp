using System.Net;
using System.Text.Json.Nodes;
using GarminMcp.Garmin;
using GarminMcp.Storage;
using GarminMcp.Sync;
using GarminMcp.Storage.Entities;
using GarminMcp.Tests.Mapping;
using GarminMcp.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace GarminMcp.Tests.Sync;

[Collection("postgres")]
public class SyncJobRunnerTests(PostgresFixture fx)
{
    private const string ListPath = "/activitylist-service/activities/search/activities";
    private const string DetailPath = "/activity-service/activity/";
    private const string FilePath = "/download-service/files/activity/";
    private const string SleepPath = "/sleep-service/sleep/dailySleepSummariesByDate";

    private static JsonNode Item(long id, int day)
    {
        var node = JsonNode.Parse(Fixture.Load("activity-list-item.json").RootElement.GetRawText())!;
        node["activityId"] = id;
        node["startTimeLocal"] = $"2022-08-{day:00} 11:47:59";
        node["startTimeGMT"] = $"2022-08-{day:00} 09:47:59";
        return node;
    }

    private async Task<SyncTestHost> CreateAsync(Func<HttpRequestMessage, HttpResponseMessage>? list = null, IReadOnlyList<HealthDataset>? datasets = null)
    {
        var host = await SyncTestHost.CreateAsync(fx, datasets);
        var items = Enumerable.Range(1, 3).Select(i => Item(1000 + i, 9 + i)).ToList();
        host.Handler.On(ListPath, list ?? (req =>
        {
            var start = int.Parse(System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query)["start"]!);
            return TestGarmin.Json(new JsonArray(items.Skip(start).Select(n => JsonNode.Parse(n.ToJsonString())).ToArray()).ToJsonString());
        }));
        host.Handler.On(DetailPath, _ => TestGarmin.Json("""{"detail":true}"""));
        host.Handler.On(FilePath, _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        host.Handler.On(SleepPath, _ => TestGarmin.Json("[]"));
        return host;
    }

    private static int DetailCalls(SyncTestHost host, long id) =>
        host.Handler.Calls.Count(c => c.RequestUri!.AbsolutePath == DetailPath + id);

    [Fact]
    public async Task Start_WhileRunning_ReturnsSameJob_StartedFalse()
    {
        var gate = new TaskCompletionSource();
        var host = await CreateAsync(list: _ =>
        {
            gate.Task.GetAwaiter().GetResult();
            return TestGarmin.Json("[]");
        });

        var first = await host.Runner.StartAsync(null, null);
        var second = await host.Runner.StartAsync(null, null);

        Assert.True(first.Started);
        Assert.False(second.Started);
        Assert.Equal(first.JobId, second.JobId);
        Assert.Equal("running", second.Status);

        gate.SetResult();
        await host.Runner.CurrentTask!;
        await using var db = PostgresFixture.OpenAnother(host.Db);
        Assert.Equal(1, await db.SyncJobs.CountAsync());
    }

    [Fact]
    public async Task Completed_StatusCompleted_LastSuccessfulSet()
    {
        var host = await CreateAsync(datasets: HealthDatasets.All.Where(d => d.Name == "sleep").ToList());

        var started = await host.Runner.StartAsync(null, null);
        await host.Runner.CurrentTask!;
        var status = await host.Runner.GetStatusAsync();

        Assert.NotNull(status);
        Assert.Equal(started.JobId, status!.JobId);
        Assert.Equal("completed", status.Status);
        Assert.Equal("health:snapshots", status.Phase);
        Assert.NotNull(status.FinishedAt);
        Assert.Equal(TimeSpan.Zero, status.FinishedAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, status.StartedAt.Offset);
        Assert.Equal(status.FinishedAt, status.LastSuccessfulSyncAt);
        Assert.Empty(status.Errors);
        Assert.NotEmpty(await PostgresFixture.OpenAnother(host.Db).HealthChunks.ToListAsync());
    }

    [Fact]
    public async Task DataAndTokenEndpoint401_AuthFailed_KeepsStoredToken_ThenNextRunResumes()
    {
        var host = await CreateAsync();
        var stored = new StoredToken(TestGarmin.Jwt(DateTimeOffset.UtcNow.AddHours(20)), DateTimeOffset.UtcNow.AddHours(20));
        host.Store.Current = stored;
        host.Handler.On(ListPath, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        host.Handler.On(TestGarmin.TokenPrefix, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await host.Runner.StartAsync(null, null);
        await host.Runner.CurrentTask!;
        var failed = await host.Runner.GetStatusAsync();
        Assert.Equal("auth_failed", failed!.Status);
        Assert.Contains("401", failed.Message);
        Assert.NotNull(failed.FinishedAt);
        Assert.Null(failed.LastSuccessfulSyncAt);
        Assert.Equal(stored, host.Store.Current);
        Assert.Equal(1, host.Handler.CountCalls(TestGarmin.TokenPrefix));

        var items = Enumerable.Range(1, 3).Select(i => Item(1000 + i, 9 + i)).ToList();
        host.Handler.On(ListPath, req =>
        {
            var start = int.Parse(System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query)["start"]!);
            return TestGarmin.Json(new JsonArray(items.Skip(start).Select(n => JsonNode.Parse(n.ToJsonString())).ToArray()).ToJsonString());
        });
        await host.Runner.StartAsync(null, null);
        await host.Runner.CurrentTask!;

        var done = await host.Runner.GetStatusAsync();
        Assert.Equal("completed", done!.Status);
        Assert.NotNull(done.LastSuccessfulSyncAt);
        Assert.Equal(1, DetailCalls(host, 1003));
        Assert.Equal(1, host.Handler.CountCalls(TestGarmin.TokenPrefix));
    }

    [Theory]
    [InlineData(true)]  // token potwierdzony przez aktywności przed 401
    [InlineData(false)] // pierwsze użycie tokenu kończy się 401
    public async Task EndpointAlways401_AtMostOneTokenIssuancePerJob_JobsComplete_OtherDatasetsStored(bool withActivities)
    {
        var host = await CreateAsync(
            list: withActivities ? null : _ => TestGarmin.Json("[]"),
            datasets: HealthDatasets.All.Where(d => d.Name is "sleep" or "hrv").ToList());
        host.Store.Current = new StoredToken(TestGarmin.Jwt(DateTimeOffset.UtcNow.AddHours(20)), DateTimeOffset.UtcNow.AddHours(20));
        host.Handler.On(SleepPath, _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        host.Handler.On("/hrv-service", _ => TestGarmin.Json("""{"hrvSummaries":[{"calendarDate":"2022-08-10","lastNightAvg":50}]}"""));

        for (var run = 1; run <= 3; run++)
        {
            var before = host.Handler.CountCalls(TestGarmin.TokenPrefix);
            await host.Runner.StartAsync(null, null);
            await host.Runner.CurrentTask!;
            var status = await host.Runner.GetStatusAsync();

            Assert.Equal("completed", status!.Status);
            Assert.Contains(status.Errors, e => e.StartsWith("sleep ") && e.Contains("HTTP 401"));
            Assert.True(host.Handler.CountCalls(TestGarmin.TokenPrefix) - before <= 1);
        }

        await using var db = PostgresFixture.OpenAnother(host.Db);
        Assert.NotEmpty(await db.Hrvs.ToListAsync());
        if (withActivities)
            Assert.Equal(3, await db.Activities.CountAsync(a => a.DetailFetchedAt != null));
    }

    [Fact]
    public async Task MalformedDetailForOneActivity_JobCompletes_ErrorRecorded_OthersFetched_HealthRuns()
    {
        var host = await CreateAsync(datasets: HealthDatasets.All.Where(d => d.Name == "sleep").ToList());
        host.Handler.On(DetailPath + "1002", _ => TestGarmin.Text("<html>maintenance</html>"));

        await host.Runner.StartAsync(null, null);
        await host.Runner.CurrentTask!;
        var status = await host.Runner.GetStatusAsync();

        Assert.Equal("completed", status!.Status);
        Assert.Contains(status.Errors, e => e.StartsWith("activity 1002: "));
        await using var db = PostgresFixture.OpenAnother(host.Db);
        Assert.Null((await db.Activities.SingleAsync(a => a.ActivityId == 1002)).DetailFetchedAt);
        Assert.NotNull((await db.Activities.SingleAsync(a => a.ActivityId == 1001)).DetailFetchedAt);
        Assert.NotNull((await db.Activities.SingleAsync(a => a.ActivityId == 1003)).DetailFetchedAt);
        Assert.NotEmpty(await db.HealthChunks.Where(c => c.Dataset == "sleep").ToListAsync());
    }

    [Fact]
    public async Task UnexpectedException_StatusFailed_MessageSet()
    {
        var host = await CreateAsync(list: _ => TestGarmin.Json("{}"));
        await host.Runner.StartAsync(null, null);
        await host.Runner.CurrentTask!;
        var status = await host.Runner.GetStatusAsync();
        Assert.Equal("failed", status!.Status);
        Assert.False(string.IsNullOrEmpty(status.Message));
        Assert.NotNull(status.FinishedAt);
    }

    [Fact]
    public async Task MarkInterrupted_SetsRunningJobsInterrupted()
    {
        var host = await CreateAsync();
        host.Db.SyncJobs.AddRange(
            new SyncJob { Status = "running", StartedAt = DateTimeOffset.UtcNow },
            new SyncJob { Status = "completed", StartedAt = DateTimeOffset.UtcNow, FinishedAt = DateTimeOffset.UtcNow });
        await host.Db.SaveChangesAsync();

        await host.Runner.MarkInterruptedAsync();

        await using var db = PostgresFixture.OpenAnother(host.Db);
        var jobs = await db.SyncJobs.OrderBy(j => j.Id).ToListAsync();
        Assert.Equal("interrupted", jobs[0].Status);
        Assert.Equal("Interrupted by restart; run sync again to resume.", jobs[0].Message);
        Assert.NotNull(jobs[0].FinishedAt);
        Assert.Equal("completed", jobs[1].Status);
    }
}
