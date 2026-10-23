using System.Text.Json;
using GarminMcp.Mapping;

namespace GarminMcp.Tests.Mapping;

public class HealthMappersTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly DateOnly Aug10 = new(2022, 8, 10);

    [Fact]
    public void DailySummaries_MapsNested()
    {
        using var doc = Fixture.Load("daily-summary.json");
        var rows = HealthMappers.DailySummaries(doc.RootElement, Now).ToList();
        Assert.Equal(2, rows.Count);
        var d = rows[0];
        Assert.Equal(Aug10, d.CalendarDate);
        Assert.Equal(9876, d.Steps);
        Assert.Equal(2450.5, d.TotalKcal);
        Assert.Equal(600, d.ActiveKcal);
        Assert.Equal(52, d.RestingHr);
        Assert.Equal(45, d.MinHr);
        Assert.Equal(170, d.MaxHr);
        Assert.Equal(31, d.AvgStress);
        Assert.Equal(88, d.MaxStress);
        Assert.Equal(12, d.BodyBatteryMin);
        Assert.Equal(95, d.BodyBatteryMax);
        Assert.Equal(30, d.ModerateMinutes);
        Assert.Equal(25, d.VigorousMinutes);
        Assert.Equal(Now, d.FetchedAt);
        Assert.Null(rows[1].RestingHr);
        Assert.Equal(5000, rows[1].Steps);
    }

    [Fact]
    public void DailySummaries_RawOutlivesSourceDocument()
    {
        var doc = Fixture.Load("daily-summary.json");
        var rows = HealthMappers.DailySummaries(doc.RootElement, Now).ToList();
        doc.Dispose();
        Assert.Equal("2022-08-10", rows[0].Raw.RootElement.GetProperty("calendarDate").GetString());
    }

    [Fact]
    public void Sleeps_MapsScoreAndStages()
    {
        using var doc = Fixture.Load("sleep.json");
        var s = Assert.Single(HealthMappers.Sleeps(doc.RootElement, Now));
        Assert.Equal(Aug10, s.CalendarDate);
        Assert.Equal(82, s.SleepScore);
        Assert.Equal(27000, s.SleepS);
        Assert.Equal(5400, s.DeepS);
        Assert.Equal(14400, s.LightS);
        Assert.Equal(5400, s.RemS);
        Assert.Equal(1800, s.AwakeS);
        Assert.Equal(50.5, s.AvgHr);
        Assert.Equal(14.2, s.AvgRespiration);
        Assert.Equal(18.5, s.AvgSleepStress);
        Assert.Equal(new DateTime(2022, 8, 9, 23, 10, 0, DateTimeKind.Unspecified), s.SleepStartLocal);
        Assert.Equal(new DateTime(2022, 8, 10, 6, 40, 0, DateTimeKind.Unspecified), s.SleepEndLocal);
    }

    [Fact]
    public void Hrvs_MapsBaseline()
    {
        using var doc = Fixture.Load("hrv.json");
        var h = Assert.Single(HealthMappers.Hrvs(doc.RootElement, Now));
        Assert.Equal(Aug10, h.CalendarDate);
        Assert.Equal(61, h.LastNightAvg);
        Assert.Equal(60, h.WeeklyAvg);
        Assert.Equal(95, h.LastNight5MinHigh);
        Assert.Equal(50, h.BaselineLow);
        Assert.Equal(70, h.BaselineHigh);
        Assert.Equal("BALANCED", h.Status);
    }

    [Fact]
    public void BodyBatteries_MinMaxFromDescriptorColumn()
    {
        using var doc = Fixture.Load("body-battery.json");
        var b = Assert.Single(HealthMappers.BodyBatteries(doc.RootElement, Now));
        Assert.Equal(Aug10, b.CalendarDate);
        Assert.Equal(60, b.Charged);
        Assert.Equal(55, b.Drained);
        Assert.Equal(12, b.Min);
        Assert.Equal(87, b.Max);
    }

    [Fact]
    public void BodyBatteries_NoDescriptor_MinMaxNull()
    {
        using var doc = JsonDocument.Parse("""[{"date":"2022-08-10","charged":1,"bodyBatteryValuesArray":[[1,"x",40]]}]""");
        var b = Assert.Single(HealthMappers.BodyBatteries(doc.RootElement, Now));
        Assert.Null(b.Min);
        Assert.Null(b.Max);
    }

    [Fact]
    public void Stresses_MapsValues()
    {
        using var doc = Fixture.Load("stress.json");
        var s = Assert.Single(HealthMappers.Stresses(doc.RootElement, Now));
        Assert.Equal(Aug10, s.CalendarDate);
        Assert.Equal(31, s.Overall);
        Assert.Equal(3600, s.HighS);
        Assert.Equal(7200, s.MediumS);
        Assert.Equal(14400, s.LowS);
        Assert.Equal(28800, s.RestS);
    }

    [Fact]
    public void Steps_MapsValues()
    {
        using var doc = Fixture.Load("steps.json");
        var s = Assert.Single(HealthMappers.Steps(doc.RootElement, Now));
        Assert.Equal(Aug10, s.CalendarDate);
        Assert.Equal(9876, s.TotalSteps);
        Assert.Equal(7500, s.TotalDistanceM);
        Assert.Equal(10000, s.StepGoal);
    }

    [Fact]
    public void Hydrations_MapsValues()
    {
        using var doc = Fixture.Load("hydration.json");
        var h = Assert.Single(HealthMappers.Hydrations(doc.RootElement, Now));
        Assert.Equal(Aug10, h.CalendarDate);
        Assert.Equal(1800, h.ValueMl);
        Assert.Equal(2500, h.GoalMl);
        Assert.Equal(400, h.SweatLossMl);
    }

    [Fact]
    public void Weights_KeyedByUtcTimestamp()
    {
        using var doc = Fixture.Load("weight.json");
        var w = Assert.Single(HealthMappers.Weights(doc.RootElement, Now));
        Assert.Equal(new DateTimeOffset(2022, 8, 10, 4, 0, 0, TimeSpan.Zero), w.TimestampUtc);
        Assert.Equal(TimeSpan.Zero, w.TimestampUtc.Offset);
        Assert.Equal(Aug10, w.CalendarDate);
        Assert.Equal(72500, w.WeightKg);
        Assert.Equal(22.4, w.Bmi);
        Assert.Equal(15.2, w.BodyFatPct);
        Assert.Equal(58.1, w.BodyWaterPct);
        Assert.Equal(33000, w.MuscleMass);
        Assert.Equal(3100, w.BoneMass);
    }

    [Fact]
    public void Weights_EpochMillisTimestamp()
    {
        using var doc = JsonDocument.Parse("""[{"calendarDate":"2022-08-10","weight":70000,"timestampGMT":1660104000000}]""");
        var w = Assert.Single(HealthMappers.Weights(doc.RootElement, Now));
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1660104000000), w.TimestampUtc);
    }

    [Fact]
    public void HeartRateDay_MapsResting()
    {
        using var doc = Fixture.Load("heart-rate-daily.json");
        var h = HealthMappers.HeartRateDay(doc.RootElement, Now);
        Assert.NotNull(h);
        Assert.Equal(Aug10, h.CalendarDate);
        Assert.Equal(52, h.Resting);
        Assert.Equal(45, h.Min);
        Assert.Equal(170, h.Max);
        Assert.Equal(53, h.SevenDayAvgResting);
    }

    [Fact]
    public void HeartRateDay_NoDate_Null()
    {
        using var doc = JsonDocument.Parse("{}");
        Assert.Null(HealthMappers.HeartRateDay(doc.RootElement, Now));
    }

    [Fact]
    public void GroupByCalendarDate_TwoItemsSameDate_OneRowWithArrayOf2()
    {
        using var doc = JsonDocument.Parse("""
            [{"calendarDate":"2022-08-10","a":1},{"calendarDate":"2022-08-10","a":2},{"calendarDate":"2022-08-11","a":3}]
            """);
        var rows = HealthMappers.GroupByCalendarDate("wellness_activities", doc.RootElement, Now).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal("wellness_activities", rows[0].Dataset);
        Assert.Equal(Aug10, rows[0].CalendarDate);
        Assert.Equal(2, rows[0].Raw.RootElement.GetArrayLength());
        Assert.Equal(1, rows[1].Raw.RootElement.GetArrayLength());
    }

    [Fact]
    public void RawDay_CloneOfRoot()
    {
        var doc = JsonDocument.Parse("""{"x":1}""");
        var r = HealthMappers.RawDay("spo2", Aug10, doc.RootElement, Now);
        doc.Dispose();
        Assert.Equal("spo2", r.Dataset);
        Assert.Equal(Aug10, r.CalendarDate);
        Assert.Equal(1, r.Raw.RootElement.GetProperty("x").GetInt32());
    }

    [Fact]
    public void Mappers_EmptyOrWrongShape_NoThrow()
    {
        using var doc = JsonDocument.Parse("""{"unexpected":true}""");
        Assert.Empty(HealthMappers.DailySummaries(doc.RootElement, Now));
        Assert.Empty(HealthMappers.Sleeps(doc.RootElement, Now));
        Assert.Empty(HealthMappers.Hrvs(doc.RootElement, Now));
        Assert.Empty(HealthMappers.Weights(doc.RootElement, Now));
    }
}
