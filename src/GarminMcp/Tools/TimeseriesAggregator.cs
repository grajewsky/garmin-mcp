using GarminMcp.Storage.Entities;

namespace GarminMcp.Tools;

public sealed record TimeseriesPoint(
    int StartS,
    int EndS,
    double? DistanceKm,
    double? HrAvg,
    double? HrMax,
    double? SpeedMps,
    double? PaceSPerKm,
    double? CadenceAvg,
    double? AltitudeAvgM,
    double? PowerAvgW);

/// <summary>Agregacja rekordów sekundowych do kubełków czasowych lub kilometrowych.</summary>
public static class TimeseriesAggregator
{
    public const double MinSpeedForPaceMps = 0.1;

    public static IReadOnlyList<TimeseriesPoint> Aggregate(IReadOnlyList<ActivityRecord> records, string resolution)
    {
        IEnumerable<IGrouping<int, ActivityRecord>> groups = resolution switch
        {
            "10s" => GroupByTime(records, 10),
            "30s" => GroupByTime(records, 30),
            "60s" => GroupByTime(records, 60),
            "km" => records.Where(r => r.DistanceM.HasValue)
                .GroupBy(r => (int)Math.Floor(r.DistanceM!.Value / 1000.0)),
            _ => throw new ArgumentException($"Unsupported resolution '{resolution}'.", nameof(resolution)),
        };

        return groups.OrderBy(g => g.Key).Select(ToPoint).ToList();
    }

    public static double? PaceFromSpeed(double? speedMps) =>
        speedMps is { } s && s >= MinSpeedForPaceMps ? Math.Round(1000.0 / s, 1) : null;

    private static IEnumerable<IGrouping<int, ActivityRecord>> GroupByTime(IReadOnlyList<ActivityRecord> records, int seconds) =>
        records.GroupBy(r => r.ElapsedS / seconds);

    private static TimeseriesPoint ToPoint(IGrouping<int, ActivityRecord> g)
    {
        var rows = g.OrderBy(r => r.ElapsedS).ToList();
        var speed = Avg(rows.Select(r => (double?)r.SpeedMps));
        return new TimeseriesPoint(
            StartS: rows[0].ElapsedS,
            EndS: rows[^1].ElapsedS,
            DistanceKm: Round(rows.LastOrDefault(r => r.DistanceM.HasValue)?.DistanceM / 1000.0, 3),
            HrAvg: Avg(rows.Select(r => (double?)r.Hr)),
            HrMax: rows.Where(r => r.Hr.HasValue).Select(r => (double?)r.Hr!.Value).Max(),
            SpeedMps: speed,
            PaceSPerKm: PaceFromSpeed(speed),
            CadenceAvg: Avg(rows.Select(r => (double?)r.Cadence)),
            AltitudeAvgM: Avg(rows.Select(r => (double?)r.AltitudeM)),
            PowerAvgW: Avg(rows.Select(r => (double?)r.PowerW)));
    }

    private static double? Avg(IEnumerable<double?> values)
    {
        var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        return present.Count == 0 ? null : Math.Round(present.Average(), 2);
    }

    private static double? Round(double? v, int digits) => v.HasValue ? Math.Round(v.Value, digits) : null;
}
