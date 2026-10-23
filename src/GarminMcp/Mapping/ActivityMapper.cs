using System.Text.Json;
using GarminMcp.Storage.Entities;

namespace GarminMcp.Mapping;

public static class ActivityMapper
{
    public static long GetId(JsonElement item) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty("activityId", out var id)
        && id.ValueKind == JsonValueKind.Number
        && id.TryGetInt64(out var v)
            ? v
            : throw new FormatException("Activity item has no numeric 'activityId'.");

    public static void ApplyListItem(Activity target, JsonElement item, DateTimeOffset fetchedAt)
    {
        target.ActivityId = GetId(item);
        target.Name = JsonRead.String(item, "activityName");
        target.Sport = JsonRead.String(item, "activityType", "typeKey");
        target.EventType = JsonRead.String(item, "eventType", "typeKey");
        target.StartTimeLocal = GarminTime.ParseLocal(
            JsonRead.String(item, "startTimeLocal") ?? throw new FormatException("Missing 'startTimeLocal'."));
        target.StartTimeUtc = GarminTime.ParseUtc(
            JsonRead.String(item, "startTimeGMT") ?? throw new FormatException("Missing 'startTimeGMT'."));
        target.DistanceM = JsonRead.Double(item, "distance");
        target.DurationS = JsonRead.Double(item, "duration");
        target.MovingDurationS = JsonRead.Double(item, "movingDuration");
        target.ElapsedDurationS = JsonRead.Double(item, "elapsedDuration");
        target.AvgHr = JsonRead.Double(item, "averageHR");
        target.MaxHr = JsonRead.Double(item, "maxHR");
        target.AvgSpeedMps = JsonRead.Double(item, "averageSpeed");
        target.MaxSpeedMps = JsonRead.Double(item, "maxSpeed");
        target.AvgCadence = JsonRead.Double(item, "averageRunningCadenceInStepsPerMinute")
            ?? JsonRead.Double(item, "averageBikingCadenceInRevPerMinute")
            ?? JsonRead.Double(item, "averageSwimCadenceInStrokesPerMinute");
        target.ElevationGainM = JsonRead.Double(item, "elevationGain");
        target.Calories = JsonRead.Double(item, "calories");
        target.AvgPowerW = JsonRead.Double(item, "avgPower");
        target.TrainingLoad = JsonRead.Double(item, "activityTrainingLoad");
        target.IntensityFactor = JsonRead.Double(item, "intensityFactor");
        target.AerobicTe = JsonRead.Double(item, "aerobicTrainingEffect");
        target.AnaerobicTe = JsonRead.Double(item, "anaerobicTrainingEffect");
        target.HrZone1S = JsonRead.Double(item, "hrTimeInZone_1");
        target.HrZone2S = JsonRead.Double(item, "hrTimeInZone_2");
        target.HrZone3S = JsonRead.Double(item, "hrTimeInZone_3");
        target.HrZone4S = JsonRead.Double(item, "hrTimeInZone_4");
        target.HrZone5S = JsonRead.Double(item, "hrTimeInZone_5");
        target.Raw = JsonRead.Clone(item);
        target.ListFetchedAt = fetchedAt;
    }
}
