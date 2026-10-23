using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class HealthChunk
{
    public string Dataset { get; set; } = null!;
    public DateOnly ChunkStart { get; set; }
    /// <summary>Nominalny koniec chunka.</summary>
    public DateOnly ChunkEnd { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
