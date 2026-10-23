using System.Text.Json;
using GarminMcp.Garmin;
using GarminMcp.Storage;
using GarminMcp.Storage.Entities;
using GarminMcp.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace GarminMcp.Tests.Storage;

[Collection("postgres")]
public class StorageTests(PostgresFixture fx)
{
    [Fact]
    public async Task Migration_Applies_AllTablesExist()
    {
        await using var db = await fx.CreateContextAsync();
        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        string[] expected =
        [
            "activities", "activity_files", "activity_laps", "activity_records", "daily_summary", "sleep", "hrv",
            "body_battery", "stress", "steps", "hydration", "weight", "heart_rate_daily", "raw_daily", "snapshots",
            "health_chunks", "sync_jobs", "auth_token"
        ];
        foreach (var t in expected) Assert.Contains(t, tables);
    }

    [Fact]
    public async Task Activity_RoundTripsRawJsonb()
    {
        await using var db = await fx.CreateContextAsync();
        db.Activities.Add(new Activity
        {
            ActivityId = 1,
            StartTimeLocal = new DateTime(2024, 5, 1, 7, 30, 0),
            StartTimeUtc = new DateTimeOffset(2024, 5, 1, 5, 30, 0, TimeSpan.Zero),
            Raw = JsonDocument.Parse("""{"activityId":1,"x":{"y":2}}"""),
            ListFetchedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await using var db2 = PostgresFixture.OpenAnother(db);
        var a = await db2.Activities.SingleAsync(x => x.ActivityId == 1);
        Assert.Equal(2, a.Raw.RootElement.GetProperty("x").GetProperty("y").GetInt32());
        Assert.Equal("pending", a.FileStatus);
        Assert.Equal(new DateTime(2024, 5, 1, 7, 30, 0), a.StartTimeLocal);
    }

    [Fact]
    public async Task DbTokenStore_SaveLoadClear()
    {
        await using var db = await fx.CreateContextAsync();
        var store = new DbTokenStore(db);
        Assert.Null(await store.LoadAsync(default));

        var exp = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await store.SaveAsync(new StoredToken("t1", exp), default);
        await store.SaveAsync(new StoredToken("t2", exp.AddHours(1)), default);

        var loaded = await store.LoadAsync(default);
        Assert.Equal(new StoredToken("t2", exp.AddHours(1)), loaded);

        await store.ClearAsync(default);
        Assert.Null(await store.LoadAsync(default));
    }

    [Fact]
    public async Task RecordWriter_Replace_RemovesOldRows()
    {
        await using var db = await fx.CreateContextAsync();
        var writer = new ActivityRecordWriter(db);
        var t0 = new DateTimeOffset(2024, 5, 1, 5, 30, 0, TimeSpan.Zero);

        List<ActivityRecord> Make(int n) => Enumerable.Range(0, n).Select(i => new ActivityRecord
        {
            ActivityId = 7,
            TimestampUtc = t0.AddSeconds(i),
            ElapsedS = i,
            Hr = i % 2 == 0 ? (short)150 : null,
            SpeedMps = 3.5f,
            Lat = 50.0,
            Lon = 19.0,
        }).ToList();

        await writer.ReplaceAsync(7, Make(10_000), default);
        Assert.Equal(10_000, await db.ActivityRecords.CountAsync(r => r.ActivityId == 7));

        await writer.ReplaceAsync(7, Make(5), default);
        Assert.Equal(5, await db.ActivityRecords.CountAsync(r => r.ActivityId == 7));
    }
}
