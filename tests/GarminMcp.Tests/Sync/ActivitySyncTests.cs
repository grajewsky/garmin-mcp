using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using GarminMcp.Garmin;
using GarminMcp.Storage;
using GarminMcp.Sync;
using GarminMcp.Tests.Mapping;
using GarminMcp.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace GarminMcp.Tests.Sync;

[Collection("postgres")]
public class ActivitySyncTests(PostgresFixture fx)
{
    private const string ListPath = "/activitylist-service/activities/search/activities";
    private const string DetailPath = "/activity-service/activity/";
    private const string FilePath = "/download-service/files/activity/";

    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private sealed class Env
    {
        public required GarminDbContext Db { get; init; }
        public required FakeGarminHandler Handler { get; init; }
        public required ActivitySync Sync { get; init; }
        public List<JsonNode> Items { get; set; } = [];
        public byte[] FitZip { get; init; } = FitFileBuilder.Zip("a.fit", FitFileBuilder.Build(
            Now.AddDays(-1), 120, i => ((byte)(120 + i % 10), 3f, 500000000 + i * 100, 100000000 + i * 100), 60));
    }

    private static JsonNode Item(long id, string name, int day)
    {
        var node = JsonNode.Parse(Fixture.Load("activity-list-item.json").RootElement.GetRawText())!;
        node["activityId"] = id;
        node["activityName"] = name;
        node["startTimeLocal"] = $"2022-08-{day:00} 11:47:59";
        node["startTimeGMT"] = $"2022-08-{day:00} 09:47:59";
        return node;
    }

    private async Task<Env> CreateAsync(int count = 3)
    {
        var db = await fx.CreateContextAsync();
        var handler = new FakeGarminHandler();
        var (client, _) = TestGarmin.Create(handler);
        var time = new FakeTime(Now);
        var env = new Env { Db = db, Handler = handler, Sync = new ActivitySync(client, db, new ActivityRecordWriter(db), time) };
        for (var i = 1; i <= count; i++) env.Items.Add(Item(1000 + i, $"Run {i}", 9 + i));

        handler.On(ListPath, req =>
        {
            var start = int.Parse(System.Web.HttpUtility.ParseQueryString(req.RequestUri!.Query)["start"]!);
            return TestGarmin.Json(new JsonArray(env.Items.Skip(start).Select(n => JsonNode.Parse(n.ToJsonString())).ToArray()).ToJsonString());
        });
        handler.On(DetailPath, _ => TestGarmin.Json("""{"detail":true}"""));
        handler.On(DetailPath + "1001/workouts", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        handler.On(FilePath, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(env.FitZip) });
        return env;
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static Task Run(Env env, RecordingProgress p, SyncScope? scope = null) =>
        env.Sync.RunAsync(scope ?? new SyncScope(null, null), p, CancellationToken.None);

    [Fact]
    public async Task FullSync_StoresActivitiesDetailsFilesAndRecords()
    {
        var env = await CreateAsync();
        var p = new RecordingProgress();
        await Run(env, p);

        await using var db = PostgresFixture.OpenAnother(env.Db);
        var acts = await db.Activities.OrderBy(a => a.ActivityId).ToListAsync();
        Assert.Equal(3, acts.Count);
        Assert.All(acts, a =>
        {
            Assert.Equal("ok", a.FileStatus);
            Assert.NotNull(a.DetailFetchedAt);
            Assert.Equal(TimeSpan.Zero, a.DetailFetchedAt!.Value.Offset);
            Assert.NotNull(a.RawDetail);
        });
        Assert.Null(acts[0].RawWorkouts);
        Assert.NotNull(acts[1].RawWorkouts);
        Assert.Equal(3, await db.ActivityFiles.CountAsync());
        Assert.Equal(360, await db.ActivityRecords.CountAsync());
        Assert.Equal(6, await db.ActivityLaps.CountAsync());
        Assert.Equal(["activities_list", "activities_detail"], p.Phases.Select(x => x.Phase));
        Assert.Equal(3, p.Phases[1].Total);
        Assert.Empty(p.Errors);
    }

    [Fact]
    public async Task SecondSync_DoesNotRefetchDetailsOrFiles()
    {
        var env = await CreateAsync();
        await Run(env, new RecordingProgress());
        env.Handler.Calls.Clear();
        await Run(env, new RecordingProgress());
        Assert.Equal(0, env.Handler.CountCalls("/activity-service/activity/"));
        Assert.Equal(0, env.Handler.CountCalls("/download-service/"));
    }

    [Fact]
    public async Task SecondSync_RenamedActivity_UpdatesName()
    {
        var env = await CreateAsync();
        await Run(env, new RecordingProgress());
        env.Items[0]["activityName"] = "Renamed";
        await Run(env, new RecordingProgress());

        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Equal("Renamed", (await db.Activities.SingleAsync(a => a.ActivityId == 1001)).Name);
        Assert.Equal(3, await db.Activities.CountAsync());
    }

    [Fact]
    public async Task ActivityMissingFromList_SetsDeletedAt_AndReappearing_ClearsIt()
    {
        var env = await CreateAsync();
        await Run(env, new RecordingProgress());
        var removed = env.Items[1];
        env.Items.RemoveAt(1);
        await Run(env, new RecordingProgress());

        await using (var db = PostgresFixture.OpenAnother(env.Db))
        {
            var a = await db.Activities.SingleAsync(x => x.ActivityId == 1002);
            Assert.Equal(Now, a.DeletedAt);
            Assert.Null((await db.Activities.SingleAsync(x => x.ActivityId == 1001)).DeletedAt);
        }

        env.Items.Insert(1, removed);
        await Run(env, new RecordingProgress());
        await using var db2 = PostgresFixture.OpenAnother(env.Db);
        Assert.Null((await db2.Activities.SingleAsync(x => x.ActivityId == 1002)).DeletedAt);
    }

    [Fact]
    public async Task RepeatedPage_ThrowsAndMarksNothingDeleted()
    {
        var env = await CreateAsync(0);
        var page = Enumerable.Range(1, 99).Select(i => Item(5000 + i, $"A{i}", 1 + i % 20)).ToList();
        env.Handler.On(ListPath, _ => TestGarmin.Json(new JsonArray(page.Select(n => JsonNode.Parse(n.ToJsonString())).ToArray()).ToJsonString()));
        env.Db.Activities.Add(NewActivity(9));
        await env.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<SyncException>(() => Run(env, new RecordingProgress()));
        Assert.Equal("Activity list paging returned a repeated page", ex.Message);

        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Null((await db.Activities.SingleAsync(a => a.ActivityId == 9)).DeletedAt);
    }

    private static GarminMcp.Storage.Entities.Activity NewActivity(long id) => new()
    {
        ActivityId = id,
        StartTimeLocal = new DateTime(2022, 1, 1),
        StartTimeUtc = new DateTimeOffset(2022, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Raw = JsonDocument.Parse("{}"),
        ListFetchedAt = Now,
    };

    [Fact]
    public async Task FileNotFound_SetsMissing_ContinuesWithNext()
    {
        var env = await CreateAsync();
        env.Handler.On(FilePath + "1002", _ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await Run(env, new RecordingProgress());

        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Equal("ok", (await db.Activities.SingleAsync(a => a.ActivityId == 1001)).FileStatus);
        Assert.Equal("missing", (await db.Activities.SingleAsync(a => a.ActivityId == 1002)).FileStatus);
        Assert.Equal("ok", (await db.Activities.SingleAsync(a => a.ActivityId == 1003)).FileStatus);
        Assert.NotNull((await db.Activities.SingleAsync(a => a.ActivityId == 1002)).DetailFetchedAt);
    }

    [Fact]
    public async Task GpxFile_SetsUnparsed()
    {
        var env = await CreateAsync(1);
        var gpx = FitFileBuilder.Zip("a.gpx", "<gpx/>"u8.ToArray());
        env.Handler.On(FilePath, _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(gpx) });
        await Run(env, new RecordingProgress());

        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Equal("unparsed", (await db.Activities.SingleAsync()).FileStatus);
        Assert.Equal("gpx", (await db.ActivityFiles.SingleAsync()).Format);
        Assert.Equal(0, await db.ActivityRecords.CountAsync());
    }

    [Fact]
    public async Task CorruptFile_SetsError_ContinuesWithNext()
    {
        var env = await CreateAsync();
        var bad = FitFileBuilder.Zip("a.fit", [1, 2, 3, 4, 5, 6, 7, 8, (byte)'.', (byte)'F', (byte)'I', (byte)'T', 9, 9]);
        env.Handler.On(FilePath + "1002", _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bad) });
        var p = new RecordingProgress();
        await Run(env, p);

        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Equal("error", (await db.Activities.SingleAsync(a => a.ActivityId == 1002)).FileStatus);
        Assert.Equal("ok", (await db.Activities.SingleAsync(a => a.ActivityId == 1003)).FileStatus);
        Assert.Single(p.Errors);
    }

    [Fact]
    public async Task MalformedListItem_RecordsError_SkipsItem_NotMarkedDeleted()
    {
        var env = await CreateAsync(2);
        await Run(env, new RecordingProgress());
        env.Items[0].AsObject().Remove("startTimeGMT");
        var p = new RecordingProgress();
        await Run(env, p);

        Assert.Single(p.Errors);
        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Null((await db.Activities.SingleAsync(a => a.ActivityId == 1001)).DeletedAt);
    }

    [Fact]
    public async Task DetailHttp500_RecordsError_LeavesActivityPending()
    {
        var env = await CreateAsync();
        env.Handler.On(DetailPath + "1002", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var p = new RecordingProgress();
        await Run(env, p);

        Assert.Equal(["activity 1002: HTTP 500"], p.Errors);
        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Null((await db.Activities.SingleAsync(a => a.ActivityId == 1002)).DetailFetchedAt);
        Assert.NotNull((await db.Activities.SingleAsync(a => a.ActivityId == 1003)).DetailFetchedAt);
    }

    [Fact]
    public async Task RangeSync_RefetchesDetailsInRange_DoesNotMarkDeletions()
    {
        var env = await CreateAsync();
        await Run(env, new RecordingProgress());
        env.Items.RemoveAt(2); // 1003 missing from (range) list
        env.Handler.Calls.Clear();
        await Run(env, new RecordingProgress(), new SyncScope(new DateOnly(2022, 8, 10), new DateOnly(2022, 8, 10)));

        Assert.Equal(2, env.Handler.CountCalls(DetailPath + "1001")); // detail + workouts
        Assert.Equal(0, env.Handler.CountCalls(DetailPath + "1002"));
        var listCall = env.Handler.Calls.First(c => c.RequestUri!.AbsolutePath == ListPath).RequestUri!.Query;
        Assert.Contains("startDate=2022-08-10", listCall);
        Assert.Contains("endDate=2022-08-10", listCall);

        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Null((await db.Activities.SingleAsync(a => a.ActivityId == 1003)).DeletedAt);
        Assert.Equal(360, await db.ActivityRecords.CountAsync()); // no duplicates
        Assert.Equal(3, await db.ActivityFiles.CountAsync());
        Assert.Equal(6, await db.ActivityLaps.CountAsync());
    }

    [Fact]
    public async Task AuthFailureMidway_Propagates_KeepsAlreadyFetched()
    {
        var env = await CreateAsync();
        var tokenCalls = 0;
        env.Handler.On(DetailPath + "1003", _ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        env.Handler.On(TestGarmin.TokenPrefix, _ =>
            ++tokenCalls == 1 ? TestGarmin.Text(TestGarmin.Jwt(Now.AddYears(50))) : new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<GarminAuthException>(() => Run(env, new RecordingProgress()));

        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.NotNull((await db.Activities.SingleAsync(a => a.ActivityId == 1001)).DetailFetchedAt);
        Assert.NotNull((await db.Activities.SingleAsync(a => a.ActivityId == 1002)).DetailFetchedAt);
        Assert.Null((await db.Activities.SingleAsync(a => a.ActivityId == 1003)).DetailFetchedAt);
    }

    [Theory]
    [InlineData(404, "")]
    [InlineData(200, "{}")]
    public async Task ListResponseNullOrNotArray_Throws_MarksNothingDeleted(int status, string body)
    {
        var env = await CreateAsync();
        await Run(env, new RecordingProgress());
        env.Handler.On(ListPath, _ => status == 404
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : TestGarmin.Json(body));

        await Assert.ThrowsAsync<SyncException>(() => Run(env, new RecordingProgress()));

        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.Equal(0, await db.Activities.CountAsync(a => a.DeletedAt != null));
    }

    [Fact]
    public async Task RangeSync_AlsoFetchesPendingActivitiesOutsideRange()
    {
        var env = await CreateAsync();
        env.Handler.On(DetailPath + "1003", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await Run(env, new RecordingProgress()); // 1003 stays pending
        env.Handler.On(DetailPath + "1003", _ => TestGarmin.Json("{}"));
        env.Handler.Calls.Clear();
        await Run(env, new RecordingProgress(), new SyncScope(new DateOnly(2022, 8, 10), new DateOnly(2022, 8, 10)));

        Assert.True(env.Handler.CountCalls(DetailPath + "1001") > 0);
        Assert.Equal(0, env.Handler.CountCalls(DetailPath + "1002"));
        Assert.True(env.Handler.CountCalls(DetailPath + "1003") > 0);
        await using var db = PostgresFixture.OpenAnother(env.Db);
        Assert.NotNull((await db.Activities.SingleAsync(a => a.ActivityId == 1003)).DetailFetchedAt);
    }
}
