using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class ActivityFile
{
    public long ActivityId { get; set; }
    public string Format { get; set; } = null!;
    public byte[] Content { get; set; } = null!;
    public int SizeBytes { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
}
