using System.Text.Json;
using GarminMcp.Storage;
using GarminMcp.Storage.Entities;
using GarminMcp.Tests.Support;
using GarminMcp.Tools;

namespace GarminMcp.Tests.Tools;

[Collection("postgres")]
public class HealthToolsTests(PostgresFixture fx)
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonDocument Raw() => JsonDocument.Parse("""{"k":"raw"}""");

    private async Task<(HealthTools Tools, GarminDbContext Db)> CreateAsync()
    {
        var db = await fx.CreateContextAsync();
        return (new HealthTools(PostgresFixture.OpenAnother(db)), db);
    }

    private static Sleep NewSleep(DateOnly d) => new()
    {
        CalendarDate = d, SleepS = 27000, DeepS = 5000, LightS = 15000, RemS = 6000, AwakeS = 1000, SleepScore = 80,
        SleepStartLocal = new DateTime(d.Year, d.Month, d.Day, 23, 0, 0).AddDays(-1),
        SleepEndLocal = new DateTime(d.Year, d.Month, d.Day, 6, 30, 0),
        Raw = Raw(), FetchedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task GetSleep_InclusiveRange_SortedAscending()
    {
        var (tools, db) = await CreateAsync();
        db.Sleeps.AddRange(
            NewSleep(new DateOnly(2026, 10, 22)), NewSleep(new DateOnly(2026, 10, 20)),
            NewSleep(new DateOnly(2026, 10, 21)), NewSleep(new DateOnly(2026, 10, 19)),
            NewSleep(new DateOnly(2026, 10, 23)));
        await db.SaveChangesAsync();

        var json = Parse(await tools.GetSleep("2026-10-20", "2026-10-22"));

        Assert.Equal("2026-10-20", json.GetProperty("from").GetString());
        Assert.Equal("2026-10-22", json.GetProperty("to").GetString());
        var days = json.GetProperty("days");
        Assert.Equal(3, days.GetArrayLength());
        Assert.Equal("2026-10-20", days[0].GetProperty("calendar_date").GetString());
        Assert.Equal("2026-10-22", days[2].GetProperty("calendar_date").GetString());
        Assert.Equal(27000, days[0].GetProperty("sleep_s").GetInt32());
        Assert.Equal("2026-10-19T23:00:00", days[0].GetProperty("sleep_start_local").GetString());
    }

    [Fact]
    public async Task GetSleep_Empty_ReturnsNoData()
    {
        var (tools, _) = await CreateAsync();

        var json = Parse(await tools.GetSleep("2001-01-01", "2001-01-03"));

        Assert.Equal(0, json.GetProperty("days").GetArrayLength());
        Assert.Contains("No data for 2001-01-01..2001-01-03", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetHrv_RangeOver366Days_ReturnsError()
    {
        var (tools, _) = await CreateAsync();

        var json = Parse(await tools.GetHrv("2025-01-01", "2026-01-02"));

        Assert.Equal("Range too long: max 366 days", json.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetWeight_ReturnsMeasurements()
    {
        var (tools, db) = await CreateAsync();
        db.Weights.AddRange(
            new Weight
            {
                TimestampUtc = new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero),
                CalendarDate = new DateOnly(2026, 10, 21), WeightKg = 80.5, Bmi = 24.1, Raw = Raw(),
                FetchedAt = DateTimeOffset.UtcNow,
            },
            new Weight
            {
                TimestampUtc = new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.Zero),
                CalendarDate = new DateOnly(2026, 10, 20), WeightKg = 81.0, Raw = Raw(),
                FetchedAt = DateTimeOffset.UtcNow,
            });
        await db.SaveChangesAsync();

        var json = Parse(await tools.GetWeight("2026-10-20", "2026-10-21"));

        var m = json.GetProperty("measurements");
        Assert.Equal(2, m.GetArrayLength());
        Assert.Equal(81.0, m[0].GetProperty("weight_kg").GetDouble());
        Assert.Equal("2026-10-21T06:00:00Z", m[1].GetProperty("timestamp_utc").GetString());
        Assert.Equal("2026-10-21", m[1].GetProperty("calendar_date").GetString());
        Assert.Equal(24.1, m[1].GetProperty("bmi").GetDouble());
    }

    [Fact]
    public async Task GetDailySummary_OmitsRaw()
    {
        var (tools, db) = await CreateAsync();
        db.DailySummaries.Add(new DailySummary
        {
            CalendarDate = new DateOnly(2026, 10, 21), Steps = 9000, RestingHr = 50, Raw = Raw(),
            FetchedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var text = await tools.GetDailySummary("2026-10-21", "2026-10-21");

        Assert.DoesNotContain("\"raw\"", text);
        Assert.DoesNotContain("fetched_at", text);
        Assert.Equal(9000, Parse(text).GetProperty("days")[0].GetProperty("steps").GetInt32());
    }

    [Fact]
    public async Task GetHeartRateZones_ReturnsLatestSnapshot()
    {
        var (tools, db) = await CreateAsync();
        db.Snapshots.AddRange(
            new Snapshot
            {
                Dataset = "hr_zones", FetchedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
                Raw = JsonDocument.Parse("""{"v":"old"}"""),
            },
            new Snapshot
            {
                Dataset = "hr_zones", FetchedAt = new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
                Raw = JsonDocument.Parse("""{"v":"new"}"""),
            });
        await db.SaveChangesAsync();

        var json = Parse(await tools.GetHeartRateZones());

        Assert.Equal("new", json.GetProperty("data").GetProperty("v").GetString());
        Assert.Equal("2026-10-20T00:00:00Z", json.GetProperty("fetched_at").GetString());
    }

    [Fact]
    public async Task GetProfile_NoSnapshot_ReturnsNoData()
    {
        var (tools, _) = await CreateAsync();

        var json = Parse(await tools.GetProfile());

        Assert.Contains("No data", json.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetStress_InvalidDate_ReturnsError()
    {
        var (tools, _) = await CreateAsync();

        var json = Parse(await tools.GetStress("nope", "2026-10-21"));

        Assert.Contains("Invalid 'from' date", json.GetProperty("error").GetString());
    }
}
