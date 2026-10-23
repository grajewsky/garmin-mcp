using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class ActivityLap
{
    public long ActivityId { get; set; }
    public int LapIndex { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public double? ElapsedS { get; set; }
    public double? DistanceM { get; set; }
    public double? AvgHr { get; set; }
    public double? MaxHr { get; set; }
    public double? AvgSpeedMps { get; set; }
    public double? AvgCadence { get; set; }
    public double? AscentM { get; set; }
}
