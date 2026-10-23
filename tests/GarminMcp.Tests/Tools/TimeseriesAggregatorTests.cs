using GarminMcp.Storage.Entities;
using GarminMcp.Tools;

namespace GarminMcp.Tests.Tools;

public class TimeseriesAggregatorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 16, 0, 0, TimeSpan.Zero);

    private static ActivityRecord Rec(int s, short? hr = null, float? speed = null, float? dist = null) => new()
    {
        ActivityId = 1,
        TimestampUtc = T0.AddSeconds(s),
        ElapsedS = s,
        Hr = hr,
        SpeedMps = speed,
        DistanceM = dist,
    };

    [Fact]
    public void Aggregate_10s_25Records_3Buckets()
    {
        var records = Enumerable.Range(0, 25).Select(i => Rec(i, (short)(100 + i))).ToList();

        var points = TimeseriesAggregator.Aggregate(records, "10s");

        Assert.Equal(3, points.Count);
        Assert.Equal(104.5, points[0].HrAvg);
        Assert.Equal(20, points[2].StartS);
        Assert.Equal(24, points[2].EndS);
    }

    [Fact]
    public void Aggregate_Km_BucketsByDistance()
    {
        var records = Enumerable.Range(0, 1001).Select(i => Rec(i, speed: 2.5f, dist: i * 2.5f)).ToList();

        var points = TimeseriesAggregator.Aggregate(records, "km");

        Assert.Equal(3, points.Count);
        Assert.Equal(400, points[0].PaceSPerKm);
    }

    [Fact]
    public void Aggregate_NullSpeed_NullPace()
    {
        var points = TimeseriesAggregator.Aggregate([Rec(0), Rec(1, speed: 0.05f)], "10s");

        Assert.Null(points[0].PaceSPerKm);
    }

    [Fact]
    public void Aggregate_BadResolution_Throws()
    {
        Assert.Throws<ArgumentException>(() => TimeseriesAggregator.Aggregate([Rec(0)], "5s"));
    }
}
