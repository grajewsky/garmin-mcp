using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using GarminMcp.Storage;
using GarminMcp.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace GarminMcp.Tools;

[McpServerToolType]
public sealed class ActivityTools(GarminDbContext db)
{
    private const int MaxLimit = 500;
    private const string ResolutionError = "resolution must be one of: 10s, 30s, 60s, km, lap";

    private record ActivitySummary(
        long ActivityId, string? Name, string? Sport, string? EventType, string StartTimeLocal,
        double? DistanceKm, double? DurationS, double? MovingDurationS, double? AvgHr, double? MaxHr,
        double? AvgSpeedKmh, double? AvgPaceSPerKm, double? AvgCadence, double? ElevationGainM, double? Calories,
        double? AvgPowerW, double? TrainingLoad, double? IntensityFactor, double? AerobicTe, double? AnaerobicTe,
        string FileStatus, bool Deleted);

    private sealed record LapView(
        int LapIndex, string StartTimeUtc, double? ElapsedS, double? DistanceKm, double? AvgHr, double? MaxHr,
        double? AvgSpeedKmh, double? AvgPaceSPerKm, double? AvgCadence, double? AscentM);

    private sealed record ActivityDetail(
        long ActivityId, string? Name, string? Sport, string? EventType, string StartTimeLocal,
        double? DistanceKm, double? DurationS, double? MovingDurationS, double? AvgHr, double? MaxHr,
        double? AvgSpeedKmh, double? AvgPaceSPerKm, double? AvgCadence, double? ElevationGainM, double? Calories,
        double? AvgPowerW, double? TrainingLoad, double? IntensityFactor, double? AerobicTe, double? AnaerobicTe,
        string FileStatus, bool Deleted,
        double?[] HrTimeInZonesS, List<LapView> Laps, JsonElement? Workouts, JsonElement? Raw, JsonElement? RawDetail)
        : ActivitySummary(ActivityId, Name, Sport, EventType, StartTimeLocal, DistanceKm, DurationS, MovingDurationS,
            AvgHr, MaxHr, AvgSpeedKmh, AvgPaceSPerKm, AvgCadence, ElevationGainM, Calories, AvgPowerW, TrainingLoad,
            IntensityFactor, AerobicTe, AnaerobicTe, FileStatus, Deleted);

    [McpServerTool(Name = "list_activities")]
    [Description("List activities from the local database, newest first. Filter by date range (YYYY-MM-DD, inclusive, by local start time), sport and distance (km). Returns total count and a page of summaries (distance in km, durations in seconds, speed in km/h, pace in seconds per km). Deleted activities are hidden unless include_deleted is true. Limit is capped at 500.")]
    public async Task<string> ListActivities(
        [Description("Range start, YYYY-MM-DD, inclusive (optional)")] string? from = null,
        [Description("Range end, YYYY-MM-DD, inclusive of the whole day (optional)")] string? to = null,
        [Description("Sport key, e.g. running, cycling (optional, case-insensitive)")] string? sport = null,
        [Description("Minimum distance in km (optional)")] double? min_distance_km = null,
        [Description("Maximum distance in km (optional)")] double? max_distance_km = null,
        [Description("Include activities deleted in Garmin Connect (default false)")] bool include_deleted = false,
        [Description("Maximum number of activities to return (default 50, max 500)")] int limit = 50,
        [Description("Number of activities to skip (default 0)")] int offset = 0)
    {
        if (!ToolJson.TryParseDate(from, out var fromDate))
            return ToolJson.Error($"Invalid 'from' date '{from}'; expected YYYY-MM-DD.");
        if (!ToolJson.TryParseDate(to, out var toDate))
            return ToolJson.Error($"Invalid 'to' date '{to}'; expected YYYY-MM-DD.");
        if (fromDate is { } f && toDate is { } t && f > t)
            return ToolJson.Error("'from' must not be after 'to'.");

        limit = Math.Clamp(limit, 1, MaxLimit);
        offset = Math.Max(offset, 0);

        var q = db.Activities.AsNoTracking().AsQueryable();
        if (fromDate is { } fd) q = q.Where(a => a.StartTimeLocal >= fd.ToDateTime(TimeOnly.MinValue));
        if (toDate is { } td) q = q.Where(a => a.StartTimeLocal < td.AddDays(1).ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrWhiteSpace(sport))
        {
            var s = sport.Trim().ToLowerInvariant();
            q = q.Where(a => a.Sport != null && a.Sport.ToLower() == s);
        }
        if (min_distance_km is { } min) q = q.Where(a => a.DistanceM >= min * 1000.0);
        if (max_distance_km is { } max) q = q.Where(a => a.DistanceM <= max * 1000.0);
        if (!include_deleted) q = q.Where(a => a.DeletedAt == null);

        var total = await q.CountAsync();
        if (total == 0) return ToolJson.NoData(DescribeRange(from, to));

        var page = await q.OrderByDescending(a => a.StartTimeLocal).ThenByDescending(a => a.ActivityId)
            .Skip(offset).Take(limit).ToListAsync();
        return ToolJson.Serialize(new { Total = total, Items = page.Select(ToSummary).ToList() });
    }

    [McpServerTool(Name = "get_activity")]
    [Description("Get one activity: summary fields plus heart rate time in zones 1-5 (seconds), laps (distance in km, speed in km/h, pace in seconds per km) and workout details. Set include_raw to also get the raw Garmin JSON (raw, raw_detail).")]
    public async Task<string> GetActivity(
        [Description("Garmin activity id")] long activity_id,
        [Description("Include raw Garmin JSON payloads (default false; large)")] bool include_raw = false)
    {
        var a = await db.Activities.AsNoTracking().FirstOrDefaultAsync(x => x.ActivityId == activity_id);
        if (a is null)
            return ToolJson.Error($"Activity {activity_id} not found. Run sync or check list_activities.");

        var laps = await db.ActivityLaps.AsNoTracking().Where(l => l.ActivityId == activity_id)
            .OrderBy(l => l.LapIndex).ToListAsync();

        var s = ToSummary(a);
        var detail = new ActivityDetail(
            s.ActivityId, s.Name, s.Sport, s.EventType, s.StartTimeLocal, s.DistanceKm, s.DurationS, s.MovingDurationS,
            s.AvgHr, s.MaxHr, s.AvgSpeedKmh, s.AvgPaceSPerKm, s.AvgCadence, s.ElevationGainM, s.Calories, s.AvgPowerW,
            s.TrainingLoad, s.IntensityFactor, s.AerobicTe, s.AnaerobicTe, s.FileStatus, s.Deleted,
            [a.HrZone1S, a.HrZone2S, a.HrZone3S, a.HrZone4S, a.HrZone5S],
            laps.Select(ToLapView).ToList(),
            a.RawWorkouts?.RootElement,
            include_raw ? a.Raw.RootElement : null,
            include_raw ? a.RawDetail?.RootElement : null);
        return ToolJson.Serialize(detail);
    }

    [McpServerTool(Name = "get_activity_timeseries")]
    [Description("Get the in-activity time series aggregated by resolution: 10s, 30s or 60s time buckets, km (one point per kilometre) or lap (one point per lap). Each point has start_s/end_s (seconds from activity start), distance_km, hr_avg/hr_max (bpm), speed_mps (m/s), pace_s_per_km (seconds per km), cadence_avg, altitude_avg_m (metres) and power_avg_w (watts).")]
    public async Task<string> GetActivityTimeseries(
        [Description("Garmin activity id")] long activity_id,
        [Description("One of: 10s, 30s, 60s, km, lap (default 10s)")] string resolution = "10s")
    {
        if (resolution is not ("10s" or "30s" or "60s" or "km" or "lap"))
            return ToolJson.Error(ResolutionError);

        var a = await db.Activities.AsNoTracking().Where(x => x.ActivityId == activity_id)
            .Select(x => new { x.FileStatus }).FirstOrDefaultAsync();
        if (a is null)
            return ToolJson.Error($"Activity {activity_id} not found. Run sync or check list_activities.");

        IReadOnlyList<TimeseriesPoint> points;
        if (resolution == "lap")
        {
            var laps = await db.ActivityLaps.AsNoTracking().Where(l => l.ActivityId == activity_id)
                .OrderBy(l => l.LapIndex).ToListAsync();
            points = LapsToPoints(laps);
        }
        else
        {
            var records = await db.ActivityRecords.AsNoTracking().Where(r => r.ActivityId == activity_id)
                .OrderBy(r => r.TimestampUtc).ToListAsync();
            points = TimeseriesAggregator.Aggregate(records, resolution);
        }

        if (points.Count == 0)
            return ToolJson.Serialize(new
            {
                ActivityId = activity_id,
                a.FileStatus,
                Message = $"No time series for this activity (file status: {a.FileStatus}).",
            });

        return ToolJson.Serialize(new { ActivityId = activity_id, Resolution = resolution, Points = points });
    }

    private static IReadOnlyList<TimeseriesPoint> LapsToPoints(List<ActivityLap> laps)
    {
        if (laps.Count == 0) return [];
        var origin = laps[0].StartTimeUtc;
        double cumulativeM = 0;
        var result = new List<TimeseriesPoint>(laps.Count);
        foreach (var l in laps)
        {
            cumulativeM += l.DistanceM ?? 0;
            var start = (int)Math.Round((l.StartTimeUtc - origin).TotalSeconds);
            var end = start + (int)Math.Round(l.ElapsedS ?? 0);
            result.Add(new TimeseriesPoint(start, end, Math.Round(cumulativeM / 1000.0, 3), l.AvgHr, l.MaxHr,
                l.AvgSpeedMps, TimeseriesAggregator.PaceFromSpeed(l.AvgSpeedMps), l.AvgCadence, null, null));
        }
        return result;
    }

    private static string DescribeRange(string? from, string? to) =>
        string.IsNullOrWhiteSpace(from) && string.IsNullOrWhiteSpace(to) ? "the requested filters"
        : $"{(string.IsNullOrWhiteSpace(from) ? "start" : from.Trim())}..{(string.IsNullOrWhiteSpace(to) ? "end" : to.Trim())}";

    private static ActivitySummary ToSummary(Activity a) => new(
        a.ActivityId, a.Name, a.Sport, a.EventType,
        a.StartTimeLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        Km(a.DistanceM), a.DurationS, a.MovingDurationS, a.AvgHr, a.MaxHr,
        Kmh(a.AvgSpeedMps), TimeseriesAggregator.PaceFromSpeed(a.AvgSpeedMps), a.AvgCadence, a.ElevationGainM,
        a.Calories, a.AvgPowerW, a.TrainingLoad, a.IntensityFactor, a.AerobicTe, a.AnaerobicTe,
        a.FileStatus, a.DeletedAt != null);

    private static LapView ToLapView(ActivityLap l) => new(
        l.LapIndex, l.StartTimeUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        l.ElapsedS, Km(l.DistanceM), l.AvgHr, l.MaxHr, Kmh(l.AvgSpeedMps),
        TimeseriesAggregator.PaceFromSpeed(l.AvgSpeedMps), l.AvgCadence, l.AscentM);

    private static double? Km(double? m) => m.HasValue ? Math.Round(m.Value / 1000.0, 3) : null;
    private static double? Kmh(double? mps) => mps.HasValue ? Math.Round(mps.Value * 3.6, 2) : null;
}
