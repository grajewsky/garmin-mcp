using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class Stress
{
    public DateOnly CalendarDate { get; set; }
    public int? Overall { get; set; }
    public int? HighS { get; set; }
    public int? MediumS { get; set; }
    public int? LowS { get; set; }
    public int? RestS { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
