using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using GarminMcp.Storage;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace GarminMcp.Tools;

[McpServerToolType]
public sealed class HealthTools(GarminDbContext db)
{
    private const int MaxDays = 366;

    private sealed record SleepView(
        string CalendarDate, int? SleepS, int? DeepS, int? LightS, int? RemS, int? AwakeS, int? SleepScore,
        double? AvgHr, double? AvgRespiration, double? AvgSleepStress, string? SleepStartLocal, string? SleepEndLocal);

    private sealed record HrvView(
        string CalendarDate, int? LastNightAvg, int? WeeklyAvg, int? LastNight5MinHigh, int? BaselineLow,
        int? BaselineHigh, string? Status);

    private sealed record BodyBatteryView(string CalendarDate, int? Charged, int? Drained, int? Min, int? Max);

    private sealed record StressView(string CalendarDate, int? Overall, int? HighS, int? MediumS, int? LowS, int? RestS);

    private sealed record DailySummaryView(
        string CalendarDate, int? Steps, double? TotalKcal, double? ActiveKcal, int? RestingHr, int? MinHr, int? MaxHr,
        int? AvgStress, int? MaxStress, int? BodyBatteryMin, int? BodyBatteryMax, int? ModerateMinutes,
        int? VigorousMinutes);

    private sealed record WeightView(
        string TimestampUtc, string CalendarDate, double? WeightKg, double? Bmi, double? BodyFatPct,
        double? BodyWaterPct, double? MuscleMass, double? BoneMass);

    [McpServerTool(Name = "get_sleep")]
    [Description("Get daily sleep data for a date range (YYYY-MM-DD, inclusive, max 366 days), sorted ascending. Durations (sleep_s, deep_s, light_s, rem_s, awake_s) are in seconds; sleep_score 0-100; avg_hr in bpm; avg_respiration in breaths per minute; sleep_start_local/sleep_end_local are local times.")]
    public Task<string> GetSleep(
        [Description("Range start, YYYY-MM-DD, inclusive")] string from,
        [Description("Range end, YYYY-MM-DD, inclusive")] string to) =>
        Days(from, to, "days", async (f, t) =>
            (await db.Sleeps.AsNoTracking().Where(x => x.CalendarDate >= f && x.CalendarDate <= t)
                .OrderBy(x => x.CalendarDate).ToListAsync())
            .Select(x => (object)new SleepView(Date(x.CalendarDate), x.SleepS, x.DeepS, x.LightS, x.RemS, x.AwakeS,
                x.SleepScore, x.AvgHr, x.AvgRespiration, x.AvgSleepStress, Local(x.SleepStartLocal),
                Local(x.SleepEndLocal))).ToList());

    [McpServerTool(Name = "get_hrv")]
    [Description("Get daily heart rate variability for a date range (YYYY-MM-DD, inclusive, max 366 days), sorted ascending. All values are in milliseconds (last_night_avg, weekly_avg, last_night_5min_high, baseline_low, baseline_high); status is the Garmin HRV status.")]
    public Task<string> GetHrv(
        [Description("Range start, YYYY-MM-DD, inclusive")] string from,
        [Description("Range end, YYYY-MM-DD, inclusive")] string to) =>
        Days(from, to, "days", async (f, t) =>
            (await db.Hrvs.AsNoTracking().Where(x => x.CalendarDate >= f && x.CalendarDate <= t)
                .OrderBy(x => x.CalendarDate).ToListAsync())
            .Select(x => (object)new HrvView(Date(x.CalendarDate), x.LastNightAvg, x.WeeklyAvg, x.LastNight5MinHigh,
                x.BaselineLow, x.BaselineHigh, x.Status)).ToList());

    [McpServerTool(Name = "get_body_battery")]
    [Description("Get daily Body Battery for a date range (YYYY-MM-DD, inclusive, max 366 days), sorted ascending. charged and drained are points; min and max are the lowest and highest level (0-100) of the day.")]
    public Task<string> GetBodyBattery(
        [Description("Range start, YYYY-MM-DD, inclusive")] string from,
        [Description("Range end, YYYY-MM-DD, inclusive")] string to) =>
        Days(from, to, "days", async (f, t) =>
            (await db.BodyBatteries.AsNoTracking().Where(x => x.CalendarDate >= f && x.CalendarDate <= t)
                .OrderBy(x => x.CalendarDate).ToListAsync())
            .Select(x => (object)new BodyBatteryView(Date(x.CalendarDate), x.Charged, x.Drained, x.Min, x.Max))
            .ToList());

    [McpServerTool(Name = "get_stress")]
    [Description("Get daily stress for a date range (YYYY-MM-DD, inclusive, max 366 days), sorted ascending. overall is the average stress level 0-100; high_s, medium_s, low_s and rest_s are time in each stress band in seconds.")]
    public Task<string> GetStress(
        [Description("Range start, YYYY-MM-DD, inclusive")] string from,
        [Description("Range end, YYYY-MM-DD, inclusive")] string to) =>
        Days(from, to, "days", async (f, t) =>
            (await db.Stresses.AsNoTracking().Where(x => x.CalendarDate >= f && x.CalendarDate <= t)
                .OrderBy(x => x.CalendarDate).ToListAsync())
            .Select(x => (object)new StressView(Date(x.CalendarDate), x.Overall, x.HighS, x.MediumS, x.LowS, x.RestS))
            .ToList());

    [McpServerTool(Name = "get_daily_summary")]
    [Description("Get daily summaries for a date range (YYYY-MM-DD, inclusive, max 366 days), sorted ascending. steps is a count; total_kcal and active_kcal in kilocalories; resting_hr, min_hr, max_hr in bpm; avg_stress and max_stress 0-100; body_battery_min/max 0-100; moderate_minutes and vigorous_minutes are intensity minutes.")]
    public Task<string> GetDailySummary(
        [Description("Range start, YYYY-MM-DD, inclusive")] string from,
        [Description("Range end, YYYY-MM-DD, inclusive")] string to) =>
        Days(from, to, "days", async (f, t) =>
            (await db.DailySummaries.AsNoTracking().Where(x => x.CalendarDate >= f && x.CalendarDate <= t)
                .OrderBy(x => x.CalendarDate).ToListAsync())
            .Select(x => (object)new DailySummaryView(Date(x.CalendarDate), x.Steps, x.TotalKcal, x.ActiveKcal,
                x.RestingHr, x.MinHr, x.MaxHr, x.AvgStress, x.MaxStress, x.BodyBatteryMin, x.BodyBatteryMax,
                x.ModerateMinutes, x.VigorousMinutes)).ToList());

    [McpServerTool(Name = "get_weight")]
    [Description("Get weight measurements for a date range (YYYY-MM-DD, inclusive by calendar date, max 366 days), sorted ascending by time. weight_kg, muscle_mass and bone_mass in kilograms; bmi in kg/m2; body_fat_pct and body_water_pct in percent; timestamp_utc is UTC.")]
    public Task<string> GetWeight(
        [Description("Range start, YYYY-MM-DD, inclusive")] string from,
        [Description("Range end, YYYY-MM-DD, inclusive")] string to) =>
        Days(from, to, "measurements", async (f, t) =>
            (await db.Weights.AsNoTracking().Where(x => x.CalendarDate >= f && x.CalendarDate <= t)
                .OrderBy(x => x.TimestampUtc).ToListAsync())
            .Select(x => (object)new WeightView(
                ToolJson.Utc(x.TimestampUtc),
                Date(x.CalendarDate), x.WeightKg, x.Bmi, x.BodyFatPct, x.BodyWaterPct, x.MuscleMass, x.BoneMass))
            .ToList());

    [McpServerTool(Name = "get_heart_rate_zones")]
    [Description("Get the user's heart rate zone configuration (zone boundaries in bpm) from the newest stored Garmin snapshot, as raw Garmin JSON in data, with fetched_at (UTC).")]
    public Task<string> GetHeartRateZones() => LatestSnapshot("hr_zones");

    [McpServerTool(Name = "get_profile")]
    [Description("Get the user's Garmin profile and settings from the newest stored snapshot, as raw Garmin JSON in data, with fetched_at (UTC).")]
    public Task<string> GetProfile() => LatestSnapshot("user_profile");

    private async Task<string> LatestSnapshot(string dataset)
    {
        var s = await db.Snapshots.AsNoTracking().Where(x => x.Dataset == dataset)
            .OrderByDescending(x => x.FetchedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync();
        if (s is null) return ToolJson.NoData(dataset);
        return ToolJson.Serialize(new
        {
            FetchedAt = ToolJson.Utc(s.FetchedAt),
            Data = s.Raw.RootElement,
        });
    }

    private static async Task<string> Days(string from, string to, string key,
        Func<DateOnly, DateOnly, Task<List<object>>> load)
    {
        if (!ToolJson.TryParseDate(from, out var f) || f is null)
            return ToolJson.Error($"Invalid 'from' date '{from}'; expected YYYY-MM-DD.");
        if (!ToolJson.TryParseDate(to, out var t) || t is null)
            return ToolJson.Error($"Invalid 'to' date '{to}'; expected YYYY-MM-DD.");
        if (f > t) return ToolJson.Error("'from' must not be after 'to'.");
        if (t.Value.DayNumber - f.Value.DayNumber + 1 > MaxDays) return ToolJson.Error("Range too long: max 366 days");

        var rows = await load(f.Value, t.Value);
        var fromText = Date(f.Value);
        var toText = Date(t.Value);
        var result = new Dictionary<string, object> { ["from"] = fromText, ["to"] = toText, [key] = rows };
        if (rows.Count == 0)
        {
            using var doc = JsonDocument.Parse(ToolJson.NoData($"{fromText}..{toText}"));
            result["message"] = doc.RootElement.GetProperty("message").GetString()!;
        }
        return ToolJson.Serialize(result);
    }

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Local(DateTime? d) => d?.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
}
