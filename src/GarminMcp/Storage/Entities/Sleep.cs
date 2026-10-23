using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class Sleep
{
    public DateOnly CalendarDate { get; set; }
    public int? SleepS { get; set; }
    public int? DeepS { get; set; }
    public int? LightS { get; set; }
    public int? RemS { get; set; }
    public int? AwakeS { get; set; }
    public int? SleepScore { get; set; }
    public double? AvgHr { get; set; }
    public double? AvgRespiration { get; set; }
    public double? AvgSleepStress { get; set; }
    public DateTime? SleepStartLocal { get; set; }
    public DateTime? SleepEndLocal { get; set; }
    public JsonDocument Raw { get; set; } = null!;
    public DateTimeOffset FetchedAt { get; set; }
}
