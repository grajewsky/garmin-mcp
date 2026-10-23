using System.Text.Json;
using GarminMcp.Storage;
using GarminMcp.Storage.Entities;
using GarminMcp.Tests.Support;
using GarminMcp.Tools;

namespace GarminMcp.Tests.Tools;

[Collection("postgres")]
public class ActivityToolsTests(PostgresFixture fx)
{
    private static Activity NewActivity(long id, string sport, DateTime startLocal, double distanceM = 10000,
        DateTimeOffset? deletedAt = null, string fileStatus = "ok") => new()
    {
        ActivityId = id,
        Name = $"Activity {id}",
        Sport = sport,
        StartTimeLocal = startLocal,
        StartTimeUtc = new DateTimeOffset(startLocal, TimeSpan.Zero),
        DistanceM = distanceM,
        DurationS = 3000,
        AvgSpeedMps = 3.0,
        Raw = JsonDocument.Parse("""{"k":"raw"}"""),
        RawDetail = JsonDocument.Parse("""{"k":"detail"}"""),
        RawWorkouts = JsonDocument.Parse("""[{"w":1}]"""),
        FileStatus = fileStatus,
        ListFetchedAt = DateTimeOffset.UtcNow,
        DeletedAt = deletedAt,
    };

    private async Task<(ActivityTools Tools, GarminDbContext Db)> CreateAsync(params Activity[] activities)
    {
        var db = await fx.CreateContextAsync();
        db.Activities.AddRange(activities);
        await db.SaveChangesAsync();
        return (new ActivityTools(GarminDbContextFor(db)), db);
    }

    private static GarminDbContext GarminDbContextFor(GarminDbContext db) => PostgresFixture.OpenAnother(db);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task ListActivities_FiltersBySportAndInclusiveDates()
    {
        var (tools, _) = await CreateAsync(
            NewActivity(1, "running", new DateTime(2026, 10, 21, 18, 0, 0)),
            NewActivity(2, "cycling", new DateTime(2026, 10, 21, 9, 0, 0)),
            NewActivity(3, "running", new DateTime(2026, 10, 22, 7, 0, 0)),
            NewActivity(4, "running", new DateTime(2026, 10, 20, 7, 0, 0)));

        var json = Parse(await tools.ListActivities(from: "2026-10-21", to: "2026-10-21", sport: "running"));

        Assert.Equal(1, json.GetProperty("total").GetInt32());
        var item = json.GetProperty("items")[0];
        Assert.Equal(1, item.GetProperty("activity_id").GetInt64());
        Assert.Equal("2026-10-21T18:00:00", item.GetProperty("start_time_local").GetString());
        Assert.Equal(10, item.GetProperty("distance_km").GetDouble());
        Assert.Equal(10.8, item.GetProperty("avg_speed_kmh").GetDouble());
        Assert.Equal(333.3, item.GetProperty("avg_pace_s_per_km").GetDouble());
        Assert.False(item.GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task ListActivities_ExcludesDeletedByDefault()
    {
        var (tools, _) = await CreateAsync(
            NewActivity(1, "running", new DateTime(2026, 10, 21, 18, 0, 0)),
            NewActivity(2, "running", new DateTime(2026, 10, 20, 18, 0, 0), deletedAt: DateTimeOffset.UtcNow));

        Assert.Equal(1, Parse(await tools.ListActivities()).GetProperty("total").GetInt32());
        var all = Parse(await tools.ListActivities(include_deleted: true));
        Assert.Equal(2, all.GetProperty("total").GetInt32());
        Assert.True(all.GetProperty("items")[1].GetProperty("deleted").GetBoolean());
    }

    [Fact]
    public async Task ListActivities_LimitCappedAt500()
    {
        var acts = Enumerable.Range(1, 505)
            .Select(i => NewActivity(i, "running", new DateTime(2026, 1, 1).AddHours(i))).ToArray();
        var (tools, _) = await CreateAsync(acts);

        var json = Parse(await tools.ListActivities(limit: 10000));

        Assert.Equal(505, json.GetProperty("total").GetInt32());
        Assert.Equal(500, json.GetProperty("items").GetArrayLength());
        Assert.Equal(505, json.GetProperty("items")[0].GetProperty("activity_id").GetInt64());
    }

    [Fact]
    public async Task ListActivities_Empty_ReturnsNoDataMessage()
    {
        var (tools, _) = await CreateAsync();

        var json = Parse(await tools.ListActivities(from: "2026-01-01", to: "2026-01-31"));

        Assert.StartsWith("No data for 2026-01-01..2026-01-31", json.GetProperty("message").GetString());
        Assert.Equal(0, json.GetProperty("total").GetInt32());
        Assert.Equal(0, json.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task ListActivities_InvalidDate_ReturnsError()
    {
        var (tools, _) = await CreateAsync();

        Assert.True(Parse(await tools.ListActivities(from: "x")).TryGetProperty("error", out _));
        Assert.True(Parse(await tools.ListActivities(from: "2026-02-01", to: "2026-01-01")).TryGetProperty("error", out _));
    }

    [Fact]
    public async Task GetActivity_Unknown_ReturnsError()
    {
        var (tools, _) = await CreateAsync();

        var json = Parse(await tools.GetActivity(42));

        Assert.Equal("Activity 42 not found. Run sync or check list_activities.", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetActivity_IncludesZonesAndLaps()
    {
        var a = NewActivity(7, "running", new DateTime(2026, 10, 21, 18, 0, 0));
        a.HrZone1S = 60; a.HrZone2S = 120; a.HrZone3S = 30; a.HrZone4S = 10; a.HrZone5S = 0;
        var (tools, db) = await CreateAsync(a);
        db.ActivityLaps.AddRange(
            new ActivityLap { ActivityId = 7, LapIndex = 1, StartTimeUtc = a.StartTimeUtc.AddSeconds(300), ElapsedS = 300, DistanceM = 1000, AvgSpeedMps = 2.5 },
            new ActivityLap { ActivityId = 7, LapIndex = 0, StartTimeUtc = a.StartTimeUtc, ElapsedS = 300, DistanceM = 1000, AvgSpeedMps = 2.5 });
        await db.SaveChangesAsync();

        var json = Parse(await tools.GetActivity(7));

        Assert.Equal(new double[] { 60, 120, 30, 10, 0 },
            json.GetProperty("hr_time_in_zones_s").EnumerateArray().Select(e => e.GetDouble()).ToArray());
        var laps = json.GetProperty("laps");
        Assert.Equal(2, laps.GetArrayLength());
        Assert.Equal(0, laps[0].GetProperty("lap_index").GetInt32());
        Assert.Equal("2026-10-21T18:00:00Z", laps[0].GetProperty("start_time_utc").GetString());
        Assert.Equal(400, laps[0].GetProperty("avg_pace_s_per_km").GetDouble());
        Assert.Equal(1, json.GetProperty("workouts")[0].GetProperty("w").GetInt32());
    }

    [Fact]
    public async Task GetActivity_RawOnlyWhenRequested()
    {
        var (tools, _) = await CreateAsync(NewActivity(7, "running", new DateTime(2026, 10, 21, 18, 0, 0)));

        var without = Parse(await tools.GetActivity(7));
        Assert.False(without.TryGetProperty("raw", out _));
        Assert.False(without.TryGetProperty("raw_detail", out _));

        var with = Parse(await tools.GetActivity(7, include_raw: true));
        Assert.Equal("raw", with.GetProperty("raw").GetProperty("k").GetString());
        Assert.Equal("detail", with.GetProperty("raw_detail").GetProperty("k").GetString());
    }

    [Fact]
    public async Task Timeseries_MissingFile_ReturnsMessageWithStatus()
    {
        var (tools, _) = await CreateAsync(NewActivity(9, "running", new DateTime(2026, 10, 21, 18, 0, 0), fileStatus: "missing"));

        var json = Parse(await tools.GetActivityTimeseries(9));

        Assert.Equal("missing", json.GetProperty("file_status").GetString());
        Assert.Equal("No time series for this activity (file status: missing).", json.GetProperty("message").GetString());
        Assert.Equal(9, json.GetProperty("activity_id").GetInt64());
    }

    [Fact]
    public async Task Timeseries_Lap_UsesLaps()
    {
        var a = NewActivity(9, "running", new DateTime(2026, 10, 21, 18, 0, 0));
        var (tools, db) = await CreateAsync(a);
        db.ActivityLaps.AddRange(
            new ActivityLap { ActivityId = 9, LapIndex = 0, StartTimeUtc = a.StartTimeUtc, ElapsedS = 300, DistanceM = 1000, AvgHr = 140, MaxHr = 150, AvgSpeedMps = 2.5 },
            new ActivityLap { ActivityId = 9, LapIndex = 1, StartTimeUtc = a.StartTimeUtc.AddSeconds(300), ElapsedS = 280, DistanceM = 1000, AvgHr = 150 });
        await db.SaveChangesAsync();

        var points = Parse(await tools.GetActivityTimeseries(9, "lap")).GetProperty("points");

        Assert.Equal(2, points.GetArrayLength());
        Assert.Equal(0, points[0].GetProperty("start_s").GetInt32());
        Assert.Equal(300, points[0].GetProperty("end_s").GetInt32());
        Assert.Equal(1, points[0].GetProperty("distance_km").GetDouble());
        Assert.Equal(400, points[0].GetProperty("pace_s_per_km").GetDouble());
        Assert.Equal(300, points[1].GetProperty("start_s").GetInt32());
        Assert.Equal(580, points[1].GetProperty("end_s").GetInt32());
        Assert.Equal(2, points[1].GetProperty("distance_km").GetDouble());
        Assert.False(points[1].TryGetProperty("pace_s_per_km", out _));
    }

    [Fact]
    public async Task Timeseries_Records_AggregatedAndBadResolutionRejected()
    {
        var a = NewActivity(9, "running", new DateTime(2026, 10, 21, 18, 0, 0));
        var (tools, db) = await CreateAsync(a);
        db.ActivityRecords.AddRange(Enumerable.Range(0, 25).Select(i => new ActivityRecord
        {
            ActivityId = 9, TimestampUtc = a.StartTimeUtc.AddSeconds(i), ElapsedS = i, Hr = (short)(100 + i),
        }));
        await db.SaveChangesAsync();

        Assert.Equal(3, Parse(await tools.GetActivityTimeseries(9)).GetProperty("points").GetArrayLength());
        Assert.Equal("resolution must be one of: 10s, 30s, 60s, km, lap",
            Parse(await tools.GetActivityTimeseries(9, "5s")).GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("running", 2)]
    [InlineData("trail_running", 2)]
    [InlineData("walking", 2)]
    [InlineData("hiking", 2)]
    [InlineData("cycling", 1)]
    public async Task Cadence_StepSportsDoubledInLapsAndTimeseries(string sport, int factor)
    {
        var a = NewActivity(9, sport, new DateTime(2026, 10, 21, 18, 0, 0));
        a.AvgCadence = 170;
        var (tools, db) = await CreateAsync(a);
        db.ActivityLaps.Add(new ActivityLap { ActivityId = 9, LapIndex = 0, StartTimeUtc = a.StartTimeUtc, ElapsedS = 20, AvgCadence = 85 });
        db.ActivityRecords.AddRange(Enumerable.Range(0, 20).Select(i => new ActivityRecord
        {
            ActivityId = 9, TimestampUtc = a.StartTimeUtc.AddSeconds(i), ElapsedS = i, Cadence = 84,
        }));
        await db.SaveChangesAsync();

        var lap = Parse(await tools.GetActivity(9)).GetProperty("laps")[0];
        var points = Parse(await tools.GetActivityTimeseries(9)).GetProperty("points");
        var lapPoint = Parse(await tools.GetActivityTimeseries(9, "lap")).GetProperty("points")[0];
        var summary = Parse(await tools.GetActivity(9));

        Assert.Equal(85 * factor, lap.GetProperty("avg_cadence").GetDouble());
        Assert.All(points.EnumerateArray(), p => Assert.Equal(84 * factor, p.GetProperty("cadence_avg").GetDouble()));
        Assert.Equal(85 * factor, lapPoint.GetProperty("cadence_avg").GetDouble());
        Assert.Equal(170, summary.GetProperty("avg_cadence").GetDouble());
    }

    [Fact]
    public async Task Timeseries_MoreThan500Points_ReturnsErrorSuggestingCoarser()
    {
        var a = NewActivity(9, "running", new DateTime(2026, 10, 21, 18, 0, 0));
        var (tools, db) = await CreateAsync(a);
        db.ActivityRecords.AddRange(Enumerable.Range(0, 5010).Select(i => new ActivityRecord
        {
            ActivityId = 9, TimestampUtc = a.StartTimeUtc.AddSeconds(i), ElapsedS = i, Hr = 120,
        }));
        await db.SaveChangesAsync();

        Assert.Equal("Too many points (501) at resolution 10s; use a coarser resolution: 30s, 60s, km or lap",
            Parse(await tools.GetActivityTimeseries(9, "10s")).GetProperty("error").GetString());
        Assert.Equal(167, Parse(await tools.GetActivityTimeseries(9, "30s")).GetProperty("points").GetArrayLength());
    }

    [Fact]
    public async Task Timeseries_Exactly500Points_Allowed()
    {
        var a = NewActivity(9, "running", new DateTime(2026, 10, 21, 18, 0, 0));
        var (tools, db) = await CreateAsync(a);
        db.ActivityRecords.AddRange(Enumerable.Range(0, 5000).Select(i => new ActivityRecord
        {
            ActivityId = 9, TimestampUtc = a.StartTimeUtc.AddSeconds(i), ElapsedS = i, Hr = 120,
        }));
        await db.SaveChangesAsync();

        Assert.Equal(ActivityTools.MaxTimeseriesPoints,
            Parse(await tools.GetActivityTimeseries(9, "10s")).GetProperty("points").GetArrayLength());
    }
}
