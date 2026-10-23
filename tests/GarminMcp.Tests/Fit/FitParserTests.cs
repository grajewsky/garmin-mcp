using System.Text;
using GarminMcp.Fit;
using GarminMcp.Tests.Support;

namespace GarminMcp.Tests.Fit;

public class FitParserTests
{
    private static readonly DateTimeOffset Start = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);

    private static byte[] Fit(int latSemi = 625_000_000) =>
        FitFileBuilder.Build(Start, 120, i => ((byte)(120 + i % 50), 3.5f, latSemi, 250_000_000), 60);

    [Fact]
    public void Parse_ZippedFit_ReturnsRecordsAndLaps()
    {
        var data = FitParser.Parse(42, FitFileBuilder.Zip("42_ACTIVITY.fit", Fit()));

        Assert.Equal("fit", data.Format);
        Assert.Equal(120, data.Records.Count);
        Assert.Equal(2, data.Laps.Count);
        Assert.Equal(0, data.Records[0].ElapsedS);
        Assert.Equal(119, data.Records[119].ElapsedS);
        Assert.All(data.Records, r => Assert.Equal(42, r.ActivityId));
        Assert.All(data.Records, r => Assert.Equal(TimeSpan.Zero, r.TimestampUtc.Offset));
        Assert.Equal(Start, data.Records[0].TimestampUtc);
        Assert.Equal((short?)120, data.Records[0].Hr);
        Assert.Equal(3.5f, data.Records[0].SpeedMps);
        Assert.Equal(new[] { 0, 1 }, data.Laps.Select(l => l.LapIndex));
        Assert.All(data.Laps, l => Assert.Equal(42, l.ActivityId));
        Assert.Equal(60, data.Laps[0].ElapsedS);
        Assert.Equal(Start, data.Laps[0].StartTimeUtc);
        Assert.Equal(Start.AddSeconds(60), data.Laps[1].StartTimeUtc);
    }

    [Fact]
    public void Parse_ConvertsSemicircles()
    {
        var data = FitParser.Parse(1, FitFileBuilder.Zip("a.fit", Fit()));

        Assert.Equal(52.386, data.Records[0].Lat!.Value, 1e-3);
        Assert.Equal(250_000_000 * 180.0 / 2147483648.0, data.Records[0].Lon!.Value, 1e-6);
    }

    [Fact]
    public void Parse_RawFitWithoutZip_Works()
    {
        var data = FitParser.Parse(1, Fit());

        Assert.Equal("fit", data.Format);
        Assert.Equal(120, data.Records.Count);
    }

    [Fact]
    public void Parse_ZipWithGpx_ReturnsUnparsedFormat()
    {
        var data = FitParser.Parse(1, FitFileBuilder.Zip("a.GPX", Encoding.UTF8.GetBytes("<gpx/>")));

        Assert.Equal("gpx", data.Format);
        Assert.Empty(data.Records);
        Assert.Empty(data.Laps);
    }

    [Fact]
    public void Parse_Garbage_ReturnsUnknown()
    {
        var data = FitParser.Parse(1, Encoding.UTF8.GetBytes("<html>404 not found</html>"));

        Assert.Equal("unknown", data.Format);
        Assert.Empty(data.Records);
        Assert.Empty(data.Laps);
    }

    [Fact]
    public void Parse_TruncatedFit_Throws()
    {
        var fit = Fit();
        var truncated = fit[..(fit.Length / 2)];

        Assert.Throws<FitParseException>(() => FitParser.Parse(1, truncated));
    }

    [Fact]
    public void Parse_ZipWithCorruptedEntryData_Throws()
    {
        var zip = FitFileBuilder.Zip("a.fit", Fit());
        var start = 30 + "a.fit".Length;
        for (var i = start; i < start + 40; i++) zip[i] ^= 0xFF;

        Assert.Throws<FitParseException>(() => FitParser.Parse(1, zip));
    }
}
