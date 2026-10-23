using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class Snapshot
{
    public long Id { get; set; }
    public string Dataset { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
    public JsonDocument Raw { get; set; } = null!;
}
