using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class RawDaily
{
    public string Dataset { get; set; } = null!;
    public DateOnly CalendarDate { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
