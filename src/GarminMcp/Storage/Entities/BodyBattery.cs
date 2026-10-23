using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class BodyBattery
{
    public DateOnly CalendarDate { get; set; }
    public int? Charged { get; set; }
    public int? Drained { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
