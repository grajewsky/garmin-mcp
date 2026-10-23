using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class HeartRateDaily
{
    public DateOnly CalendarDate { get; set; }
    public int? Resting { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
    public int? SevenDayAvgResting { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
