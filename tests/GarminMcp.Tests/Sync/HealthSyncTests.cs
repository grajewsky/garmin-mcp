using System.Net;
using System.Text.Json;
using GarminMcp.Garmin;
using GarminMcp.Storage;
using GarminMcp.Sync;
using GarminMcp.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GarminMcp.Tests.Sync;

[Collection("postgres")]
public class HealthSyncTests(PostgresFixture fx)
{
    private const string SleepPath = "/sleep-service/sleep/dailySleepSummariesByDate";
    private const string HrPath = "/wellness-service/wellness/dailyHeartRate";
    private const string WellnessPath = "/wellnessactivity-service/activity/summary/range/";

    private static readonly DateOnly HistoryStart = new(2022, 1, 1);
    private static readonly DateTimeOffset Today = new(2022, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Env
    {
        public required GarminDbContext Db { get; init; }
        public required FakeGarminHandler Handler { get; init; }
        public required HealthSync Sync { get; init; }
        public RecordingProgress Progress { get; } = new();
    }

    private async Task<Env> CreateAsync()
    {
        var db = await fx.CreateContextAsync();
        var handler = new FakeGarminHandler();
        var time = new FixedTime(Today);
        var (client, _) = TestGarmin.Create(handler, time: time);
        return new Env { Db = db, Handler = handler, Sync = new HealthSync(client, db, Options.Create(new GarminOptions()), time) };
    }

    private static IReadOnlyList<HealthDataset> Only(string name) => HealthDatasets.All.Where(d => d.Name == name).ToList();

    private static string SleepBody(string date, int sleepSeconds = 27000) =>
        $$"""[{"calendarDate":"{{date}}","sleepTimeSeconds":{{sleepSeconds}}}]""";

    private static string Query(HttpRequestMessage r, string key) =>
        System.Web.HttpUtility.ParseQueryString(r.RequestUri!.Query)[key]!;

    private static void SleepEchoesStart(Env env) =>
        env.Handler.On(SleepPath, r => TestGarmin.Json(SleepBody(Query(r, "startDate"))));

    private static Task Run(Env env, SyncScope? scope = null, string dataset = "sleep") =>
        env.Sync.RunAsync(scope ?? new SyncScope(null, null), HistoryStart, env.Progress, CancellationToken.None, Only(dataset));

    private static List<HttpRequestMessage> Requests(Env env, string prefix) =>
        env.Handler.Calls.Where(c => c.RequestUri!.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    [Fact]
    public async Task FirstRun_Sleep_FetchesAllChunks_StoresRows()
    {
        var env = await CreateAsync();
        SleepEchoesStart(env);

        await Run(env);

        var reqs = Requests(env, SleepPath);
        Assert.Equal(
            [("2022-01-01", "2022-01-31"), ("2022-02-01", "2022-03-03"), ("2022-03-04", "2022-03-10")],
            reqs.Select(r => (Query(r, "startDate"), Query(r, "endDate"))));
        await using var read = PostgresFixture.OpenAnother(env.Db);
        var dates = await read.Sleeps.OrderBy(s => s.CalendarDate).Select(s => s.CalendarDate).ToListAsync();
        Assert.Equal([new DateOnly(2022, 1, 1), new DateOnly(2022, 2, 1), new DateOnly(2022, 3, 4)], dates);
        Assert.Equal(3, await read.HealthChunks.CountAsync(c => c.Dataset == "sleep"));
        Assert.Equal(("health:sleep", 3), env.Progress.Phases.First());
        Assert.Equal(0, (await read.Sleeps.FirstAsync()).FetchedAt.Offset.Ticks);
    }

    [Fact]
    public async Task RequestEnd_NeverAfterToday()
    {
        var env = await CreateAsync();
        SleepEchoesStart(env);

        await Run(env);

        Assert.Equal("2022-03-10", Query(Requests(env, SleepPath).Last(), "endDate"));
    }

    [Fact]
    public async Task SecondRun_SkipsFinalChunks_RefetchesTail()
    {
        var env = await CreateAsync();
        SleepEchoesStart(env);
        await Run(env);
        env.Handler.Calls.Clear();

        await Run(env);

        var reqs = Requests(env, SleepPath);
        Assert.Single(reqs);
        Assert.Equal("2022-03-04", Query(reqs[0], "startDate"));
    }

    [Fact]
    public async Task Force_RefetchesChunksOverlappingRange()
    {
        var env = await CreateAsync();
        SleepEchoesStart(env);
        await Run(env);
        env.Handler.Calls.Clear();

        await Run(env, new SyncScope(new DateOnly(2022, 1, 5), new DateOnly(2022, 1, 6)));

        var reqs = Requests(env, SleepPath);
        Assert.Single(reqs);
        Assert.Equal("2022-01-01", Query(reqs[0], "startDate"));
        Assert.Equal("2022-01-06", Query(reqs[0], "endDate")); // przycięte do scope.To
    }

    [Fact]
    public async Task NullResponse_RecordsChunk_NoRows()
    {
        var env = await CreateAsync();
        env.Handler.On(SleepPath, _ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await Run(env);

        await using var read = PostgresFixture.OpenAnother(env.Db);
        Assert.Empty(await read.Sleeps.ToListAsync());
        Assert.Equal(3, await read.HealthChunks.CountAsync(c => c.Dataset == "sleep"));
        Assert.Empty(env.Progress.Errors);
    }

    [Fact]
    public async Task PerDayDataset_OneRequestPerDay()
    {
        var env = await CreateAsync();
        env.Handler.On(HrPath, r => TestGarmin.Json(
            $$"""{"calendarDate":"{{Query(r, "date")}}","restingHeartRate":52}"""));

        await env.Sync.RunAsync(new SyncScope(new DateOnly(2022, 3, 1), new DateOnly(2022, 3, 3)), HistoryStart,
            env.Progress, CancellationToken.None, Only("heart_rate_daily"));

        Assert.Equal(["2022-03-01", "2022-03-02", "2022-03-03"], Requests(env, HrPath).Select(r => Query(r, "date")));
        await using var read = PostgresFixture.OpenAnother(env.Db);
        Assert.Equal(3, await read.HeartRateDailies.CountAsync());
    }

    [Fact]
    public async Task WellnessActivities_GroupedByDate()
    {
        var env = await CreateAsync();
        env.Handler.On(WellnessPath, _ => TestGarmin.Json(
            """[{"calendarDate":"2022-03-04","v":1},{"calendarDate":"2022-03-04","v":2},{"calendarDate":"2022-03-05","v":3}]"""));

        await Run(env, new SyncScope(new DateOnly(2022, 3, 4), new DateOnly(2022, 3, 10)), "wellness_activities");

        await using var read = PostgresFixture.OpenAnother(env.Db);
        var rows = await read.RawDailies.Where(r => r.Dataset == "wellness_activities").OrderBy(r => r.CalendarDate).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Raw.RootElement.GetArrayLength());
        Assert.Equal(1, rows[1].Raw.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Upsert_OverwritesExistingRow()
    {
        var env = await CreateAsync();
        var seconds = 27000;
        env.Handler.On(SleepPath, _ => TestGarmin.Json(SleepBody("2022-01-10", seconds)));
        var scope = new SyncScope(new DateOnly(2022, 1, 1), new DateOnly(2022, 1, 31));
        await Run(env, scope);

        seconds = 30000;
        await Run(env, scope);

        await using var read = PostgresFixture.OpenAnother(env.Db);
        var row = Assert.Single(await read.Sleeps.ToListAsync());
        Assert.Equal(30000, row.SleepS);
        Assert.Equal(30000, row.Raw.RootElement.GetProperty("sleepTimeSeconds").GetInt32());
    }

    [Fact]
    public async Task ChunkHttp500_AddsError_ChunkNotRecorded()
    {
        var env = await CreateAsync();
        env.Handler.On(SleepPath, r => Query(r, "startDate") == "2022-02-01"
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : TestGarmin.Json(SleepBody(Query(r, "startDate"))));

        await Run(env);

        await using var read = PostgresFixture.OpenAnother(env.Db);
        Assert.Equal([new DateOnly(2022, 1, 1), new DateOnly(2022, 3, 4)],
            await read.HealthChunks.OrderBy(c => c.ChunkStart).Select(c => c.ChunkStart).ToListAsync());
        Assert.Single(env.Progress.Errors);
        Assert.Equal(2, await read.Sleeps.CountAsync());
    }

    [Fact]
    public async Task MalformedChunk_AddsError_ChunkNotRecorded_SyncContinues()
    {
        var env = await CreateAsync();
        env.Handler.On(SleepPath, r => Query(r, "startDate") == "2022-02-01"
            ? TestGarmin.Json("""{"unexpected":true}""")
            : TestGarmin.Json(SleepBody(Query(r, "startDate"))));

        await Run(env);

        await using var read = PostgresFixture.OpenAnother(env.Db);
        Assert.Equal(2, await read.HealthChunks.CountAsync());
        Assert.Single(env.Progress.Errors);
    }

    [Fact]
    public async Task Snapshots_StoredEachRun()
    {
        var env = await CreateAsync();
        env.Handler.On("/biometric-service/heartRateZones/v2", _ => TestGarmin.Json("""[{"zone":1}]"""));
        env.Handler.On("/userprofile-service/userprofile/settings", _ => TestGarmin.Json("""{"a":1}"""));
        env.Handler.On("/fitnessage-service/fitnessage/v2/2022-03-10", _ => TestGarmin.Json("""{"fitnessAge":30}"""));
        // weight_goals: 404 -> pomijany

        await Run(env);
        await Run(env);

        await using var read = PostgresFixture.OpenAnother(env.Db);
        var counts = await read.Snapshots.GroupBy(s => s.Dataset).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        Assert.Equal(3, counts.Count);
        Assert.All(counts.Values, n => Assert.Equal(2, n));
        Assert.DoesNotContain("weight_goals", counts.Keys);
    }
}
