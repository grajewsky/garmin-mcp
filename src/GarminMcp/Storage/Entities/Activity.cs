using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class Activity
{
    public long ActivityId { get; set; }
    public string? Name { get; set; }
    public string? Sport { get; set; }
    public string? EventType { get; set; }
    public DateTime StartTimeLocal { get; set; }
    public DateTimeOffset StartTimeUtc { get; set; }
    public double? DistanceM { get; set; }
    public double? DurationS { get; set; }
    public double? MovingDurationS { get; set; }
    public double? ElapsedDurationS { get; set; }
    public double? AvgHr { get; set; }
    public double? MaxHr { get; set; }
    public double? AvgSpeedMps { get; set; }
    public double? MaxSpeedMps { get; set; }
    public double? AvgCadence { get; set; }
    public double? ElevationGainM { get; set; }
    public double? Calories { get; set; }
    public double? AvgPowerW { get; set; }
    public double? TrainingLoad { get; set; }
    public double? IntensityFactor { get; set; }
    public double? AerobicTe { get; set; }
    public double? AnaerobicTe { get; set; }
    public double? HrZone1S { get; set; }
    public double? HrZone2S { get; set; }
    public double? HrZone3S { get; set; }
    public double? HrZone4S { get; set; }
    public double? HrZone5S { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public JsonDocument? RawDetail { get; set; }
    public JsonDocument? RawWorkouts { get; set; }
    /// <summary>pending|ok|missing|unparsed|error</summary>
    public string FileStatus { get; set; } = "pending";
    public DateTimeOffset ListFetchedAt { get; set; }
    public DateTimeOffset? DetailFetchedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
