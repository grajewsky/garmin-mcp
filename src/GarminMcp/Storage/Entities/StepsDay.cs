using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class StepsDay
{
    public DateOnly CalendarDate { get; set; }
    public int? TotalSteps { get; set; }
    public int? TotalDistanceM { get; set; }
    public int? StepGoal { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
