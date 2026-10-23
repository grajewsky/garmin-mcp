using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class Hrv
{
    public DateOnly CalendarDate { get; set; }
    public int? LastNightAvg { get; set; }
    public int? WeeklyAvg { get; set; }
    public int? LastNight5MinHigh { get; set; }
    public int? BaselineLow { get; set; }
    public int? BaselineHigh { get; set; }
    public string? Status { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
