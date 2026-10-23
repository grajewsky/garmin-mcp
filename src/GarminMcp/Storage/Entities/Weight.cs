using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class Weight
{
    public DateTimeOffset TimestampUtc { get; set; }
    public DateOnly CalendarDate { get; set; }
    public double? WeightKg { get; set; }
    public double? Bmi { get; set; }
    public double? BodyFatPct { get; set; }
    public double? BodyWaterPct { get; set; }
    public double? MuscleMass { get; set; }
    public double? BoneMass { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
