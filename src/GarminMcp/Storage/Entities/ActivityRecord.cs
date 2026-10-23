using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class ActivityRecord
{
    public long ActivityId { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public int ElapsedS { get; set; }
    public short? Hr { get; set; }
    public float? SpeedMps { get; set; }
    public short? Cadence { get; set; }
    public float? AltitudeM { get; set; }
    public short? PowerW { get; set; }
    public float? DistanceM { get; set; }
    public double? Lat { get; set; }
    public double? Lon { get; set; }
}
