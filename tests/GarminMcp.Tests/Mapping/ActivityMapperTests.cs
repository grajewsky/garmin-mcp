using System.Text.Json;
using GarminMcp.Mapping;
using GarminMcp.Storage.Entities;

namespace GarminMcp.Tests.Mapping;

public class ActivityMapperTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void ActivityMapper_MapsAllColumns()
    {
        using var doc = Fixture.Load("activity-list-item.json");
        var a = new Activity();
        ActivityMapper.ApplyListItem(a, doc.RootElement, Now);

        Assert.Equal(1000000001L, ActivityMapper.GetId(doc.RootElement));
        Assert.Equal(1000000001L, a.ActivityId);
        Assert.Equal("Morning Run", a.Name);
        Assert.Equal("running", a.Sport);
        Assert.Equal("training", a.EventType);
        Assert.Equal(new DateTime(2022, 8, 10, 11, 47, 59, DateTimeKind.Unspecified), a.StartTimeLocal);
        Assert.Equal(TimeSpan.Zero, a.StartTimeUtc.Offset);
        Assert.Equal(9, a.StartTimeUtc.Hour);
        Assert.Equal(10120, a.DistanceM);
        Assert.Equal(3300.5, a.DurationS);
        Assert.Equal(3250.2, a.MovingDurationS);
        Assert.Equal(3400, a.ElapsedDurationS);
        Assert.Equal(152, a.AvgHr);
        Assert.Equal(178, a.MaxHr);
        Assert.Equal(3.07, a.AvgSpeedMps);
        Assert.Equal(4.5, a.MaxSpeedMps);
        Assert.Equal(168, a.AvgCadence);
        Assert.Equal(85.5, a.ElevationGainM);
        Assert.Equal(720, a.Calories);
        Assert.Equal(240, a.AvgPowerW);
        Assert.Equal(150.3, a.TrainingLoad);
        Assert.Equal(0.9, a.IntensityFactor);
        Assert.Equal(3.5, a.AerobicTe);
        Assert.Equal(1.2, a.AnaerobicTe);
        Assert.Equal(300, a.HrZone1S);
        Assert.Equal(900, a.HrZone2S);
        Assert.Equal(1200, a.HrZone3S);
        Assert.Equal(700, a.HrZone4S);
        Assert.Equal(200, a.HrZone5S);
        Assert.Equal(Now, a.ListFetchedAt);
    }

    [Fact]
    public void ActivityMapper_RawIsIndependentClone()
    {
        var a = new Activity();
        var doc = Fixture.Load("activity-list-item.json");
        ActivityMapper.ApplyListItem(a, doc.RootElement, Now);
        doc.Dispose();
        Assert.Equal(1000000001L, a.Raw.RootElement.GetProperty("activityId").GetInt64());
    }

    [Fact]
    public void ActivityMapper_BikeCadenceFallback()
    {
        using var doc = JsonDocument.Parse("""
            {"activityId":2,"startTimeLocal":"2022-08-10 11:00:00","startTimeGMT":"2022-08-10 09:00:00",
             "averageBikingCadenceInRevPerMinute":85}
            """);
        var a = new Activity();
        ActivityMapper.ApplyListItem(a, doc.RootElement, Now);
        Assert.Equal(85, a.AvgCadence);
    }

    [Fact]
    public void ActivityMapper_MissingOptionalFields_Null()
    {
        using var doc = JsonDocument.Parse("""
            {"activityId":3,"startTimeLocal":"2022-08-10 11:00:00","startTimeGMT":"2022-08-10 09:00:00"}
            """);
        var a = new Activity();
        ActivityMapper.ApplyListItem(a, doc.RootElement, Now);
        Assert.Null(a.Name);
        Assert.Null(a.Sport);
        Assert.Null(a.EventType);
        Assert.Null(a.DistanceM);
        Assert.Null(a.AvgHr);
        Assert.Null(a.AvgCadence);
        Assert.Null(a.AvgPowerW);
        Assert.Null(a.TrainingLoad);
        Assert.Null(a.HrZone1S);
        Assert.Null(a.HrZone5S);
    }
}
