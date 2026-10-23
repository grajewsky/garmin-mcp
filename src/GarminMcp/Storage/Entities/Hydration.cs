using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class Hydration
{
    public DateOnly CalendarDate { get; set; }
    public double? ValueMl { get; set; }
    public double? GoalMl { get; set; }
    public double? SweatLossMl { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
