using GarminMcp.Mapping;

namespace GarminMcp.Tests.Mapping;

public class GarminTimeTests
{
    [Fact]
    public void ParseUtc_NoOffset_IsUtc()
    {
        var t = GarminTime.ParseUtc("2022-08-10T09:47:59.031");
        Assert.Equal(TimeSpan.Zero, t.Offset);
        Assert.Equal(9, t.Hour);
    }

    [Fact]
    public void ParseUtc_WithZ_IsUtc()
    {
        var t = GarminTime.ParseUtc("2022-08-10T09:47:59.031Z");
        Assert.Equal(TimeSpan.Zero, t.Offset);
        Assert.Equal(9, t.Hour);
    }

    [Fact]
    public void ParseUtc_WithOffset_ConvertedToUtc()
    {
        var t = GarminTime.ParseUtc("2022-08-10T11:47:59+02:00");
        Assert.Equal(TimeSpan.Zero, t.Offset);
        Assert.Equal(9, t.Hour);
    }

    [Fact]
    public void ParseUtc_SpaceSeparator_IsUtc()
    {
        var t = GarminTime.ParseUtc("2022-08-10 09:47:59");
        Assert.Equal(TimeSpan.Zero, t.Offset);
        Assert.Equal(9, t.Hour);
    }

    [Fact]
    public void ParseLocal_KindUnspecified()
    {
        var t = GarminTime.ParseLocal("2022-08-10T11:47:59.000");
        Assert.Equal(DateTimeKind.Unspecified, t.Kind);
        Assert.Equal(11, t.Hour);
    }

    [Fact]
    public void ParseLocal_SpaceSeparator_KeepsClockTime()
    {
        var t = GarminTime.ParseLocal("2022-08-10 11:47:59");
        Assert.Equal(DateTimeKind.Unspecified, t.Kind);
        Assert.Equal(11, t.Hour);
    }
}
