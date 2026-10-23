using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class SyncJob
{
    public long Id { get; set; }
    /// <summary>running|completed|failed|auth_failed|interrupted</summary>
    public string Status { get; set; } = "running";
    public string? Phase { get; set; }
    public DateOnly? RangeFrom { get; set; }
    public DateOnly? RangeTo { get; set; }
    public bool Force { get; set; }
    public int ItemsDone { get; set; }
    public int ItemsTotal { get; set; }
    /// <summary>Tablica stringów, maks. 50 ostatnich.</summary>
    public JsonDocument Errors { get; set; } = JsonDocument.Parse("[]");
    public string? Message { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
