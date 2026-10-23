using GarminMcp.Garmin;

namespace GarminMcp.Tests.Garmin;

public class DateChunkerTests
{
    [Fact] public void Chunk_AnchoredSpans_LastClippedToTo() {
        var r = DateChunker.Chunk(new(2021,9,23), new(2021,9,23), new(2021,11,30), 31);
        Assert.Equal(new ChunkSpan[] {
            new(new(2021,9,23), new(2021,10,23), new(2021,10,23)),
            new(new(2021,10,24), new(2021,11,23), new(2021,11,23)),
            new(new(2021,11,24), new(2021,12,24), new(2021,11,30)) }, r);
    }
    [Fact] public void Chunk_SubRange_ReturnsOnlyOverlappingAnchoredChunk() {
        var r = DateChunker.Chunk(new(2021,9,23), new(2021,10,30), new(2021,11,1), 31);
        Assert.Single(r); Assert.Equal(new DateOnly(2021,10,24), r[0].Start); Assert.Equal(new DateOnly(2021,11,1), r[0].RequestEnd);
    }
    [Fact] public void Chunk_OneDay_ReturnsEachDay() => Assert.Equal(3, DateChunker.Chunk(new(2022,1,1), new(2022,1,1), new(2022,1,3), 1).Count);
    [Fact] public void Chunk_FromAfterTo_Empty() => Assert.Empty(DateChunker.Chunk(new(2022,1,1), new(2022,2,1), new(2022,1,1), 31));
    [Theory]
    [InlineData("2022-01-31", null, false, true)]
    [InlineData("2022-01-31", "2022-02-06T23:00:00Z", false, true)]   // < end+7
    [InlineData("2022-01-31", "2022-02-07T00:00:00Z", false, false)]  // == end+7 → final
    [InlineData("2022-01-31", "2023-01-01T00:00:00Z", true, true)]
    public void NeedsFetch(string end, string? fetched, bool force, bool expected) =>
        Assert.Equal(expected, DateChunker.NeedsFetch(DateOnly.Parse(end), fetched is null ? null : DateTimeOffset.Parse(fetched), force, 7));
}
