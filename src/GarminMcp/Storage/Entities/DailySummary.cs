using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class DailySummary
{
    public DateOnly CalendarDate { get; set; }
    public int? Steps { get; set; }
    public double? TotalKcal { get; set; }
    public double? ActiveKcal { get; set; }
    public int? RestingHr { get; set; }
    public int? MinHr { get; set; }
    public int? MaxHr { get; set; }
    public int? AvgStress { get; set; }
    public int? MaxStress { get; set; }
    public int? BodyBatteryMin { get; set; }
    public int? BodyBatteryMax { get; set; }
    public int? ModerateMinutes { get; set; }
    public int? VigorousMinutes { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
