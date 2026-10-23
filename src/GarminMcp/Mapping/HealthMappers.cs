using System.Globalization;
using System.Text.Json;
using GarminMcp.Storage.Entities;

namespace GarminMcp.Mapping;

public static class HealthMappers
{
    public static IEnumerable<DailySummary> DailySummaries(JsonElement root, DateTimeOffset fetchedAt) =>
        Items(JsonRead.Path(root, "data")).Select(i => (i, d: JsonRead.Date(i, "calendarDate")))
            .Where(x => x.d is not null)
            .Select(x => new DailySummary
            {
                CalendarDate = x.d!.Value,
                Steps = JsonRead.Int(x.i, "movement", "steps"),
                TotalKcal = JsonRead.Double(x.i, "calories", "burnedTotal"),
                ActiveKcal = JsonRead.Double(x.i, "calories", "burnedActive"),
                RestingHr = JsonRead.Int(x.i, "heartRate", "restingValue"),
                MinHr = JsonRead.Int(x.i, "heartRate", "minValue"),
                MaxHr = JsonRead.Int(x.i, "heartRate", "maxValue"),
                AvgStress = JsonRead.Int(x.i, "stress", "avgLevel"),
                MaxStress = JsonRead.Int(x.i, "stress", "maxLevel"),
                BodyBatteryMin = JsonRead.Int(x.i, "bodyBattery", "minValue"),
                BodyBatteryMax = JsonRead.Int(x.i, "bodyBattery", "maxValue"),
                ModerateMinutes = JsonRead.Int(x.i, "intensityMinutes", "moderate"),
                VigorousMinutes = JsonRead.Int(x.i, "intensityMinutes", "vigorous"),
                Raw = JsonRead.Clone(x.i),
                FetchedAt = fetchedAt,
            });

    public static IEnumerable<Sleep> Sleeps(JsonElement root, DateTimeOffset fetchedAt) =>
        Dated(root, "calendarDate").Select(x => new Sleep
        {
            CalendarDate = x.date,
            SleepS = JsonRead.Int(x.item, "sleepTimeSeconds"),
            DeepS = JsonRead.Int(x.item, "deepSleepSeconds"),
            LightS = JsonRead.Int(x.item, "lightSleepSeconds"),
            RemS = JsonRead.Int(x.item, "remSleepSeconds"),
            AwakeS = JsonRead.Int(x.item, "awakeSleepSeconds"),
            SleepScore = JsonRead.Int(x.item, "sleepScores", "overall", "value"),
            AvgHr = JsonRead.Double(x.item, "avgHeartRate"),
            AvgRespiration = JsonRead.Double(x.item, "averageRespirationValue"),
            AvgSleepStress = JsonRead.Double(x.item, "avgSleepStress"),
            SleepStartLocal = LocalTime(x.item, "sleepStartTimestampLocal"),
            SleepEndLocal = LocalTime(x.item, "sleepEndTimestampLocal"),
            Raw = JsonRead.Clone(x.item),
            FetchedAt = fetchedAt,
        });

    public static IEnumerable<Hrv> Hrvs(JsonElement root, DateTimeOffset fetchedAt) =>
        Dated(JsonRead.Path(root, "hrvSummaries") ?? default, "calendarDate").Select(x => new Hrv
        {
            CalendarDate = x.date,
            LastNightAvg = JsonRead.Int(x.item, "lastNightAvg"),
            WeeklyAvg = JsonRead.Int(x.item, "weeklyAvg"),
            LastNight5MinHigh = JsonRead.Int(x.item, "lastNight5MinHigh"),
            BaselineLow = JsonRead.Int(x.item, "baseline", "balancedLow"),
            BaselineHigh = JsonRead.Int(x.item, "baseline", "balancedUpper"),
            Status = JsonRead.String(x.item, "status"),
            Raw = JsonRead.Clone(x.item),
            FetchedAt = fetchedAt,
        });

    public static IEnumerable<BodyBattery> BodyBatteries(JsonElement root, DateTimeOffset fetchedAt) =>
        Dated(root, "date").Select(x =>
        {
            var (min, max) = BodyBatteryRange(x.item);
            return new BodyBattery
            {
                CalendarDate = x.date,
                Charged = JsonRead.Int(x.item, "charged"),
                Drained = JsonRead.Int(x.item, "drained"),
                Min = min,
                Max = max,
                Raw = JsonRead.Clone(x.item),
                FetchedAt = fetchedAt,
            };
        });

    public static IEnumerable<Stress> Stresses(JsonElement root, DateTimeOffset fetchedAt) =>
        Dated(root, "calendarDate").Select(x => new Stress
        {
            CalendarDate = x.date,
            Overall = JsonRead.Int(x.item, "values", "overallStressLevel"),
            HighS = JsonRead.Int(x.item, "values", "highStressDuration"),
            MediumS = JsonRead.Int(x.item, "values", "mediumStressDuration"),
            LowS = JsonRead.Int(x.item, "values", "lowStressDuration"),
            RestS = JsonRead.Int(x.item, "values", "restStressDuration"),
            Raw = JsonRead.Clone(x.item),
            FetchedAt = fetchedAt,
        });

    public static IEnumerable<StepsDay> Steps(JsonElement root, DateTimeOffset fetchedAt) =>
        Dated(root, "calendarDate").Select(x => new StepsDay
        {
            CalendarDate = x.date,
            TotalSteps = JsonRead.Int(x.item, "totalSteps"),
            TotalDistanceM = JsonRead.Int(x.item, "totalDistance"),
            StepGoal = JsonRead.Int(x.item, "stepGoal"),
            Raw = JsonRead.Clone(x.item),
            FetchedAt = fetchedAt,
        });

    public static IEnumerable<Hydration> Hydrations(JsonElement root, DateTimeOffset fetchedAt) =>
        Dated(root, "calendarDate").Select(x => new Hydration
        {
            CalendarDate = x.date,
            ValueMl = JsonRead.Double(x.item, "valueInML"),
            GoalMl = JsonRead.Double(x.item, "goalInML"),
            SweatLossMl = JsonRead.Double(x.item, "sweatLossInML"),
            Raw = JsonRead.Clone(x.item),
            FetchedAt = fetchedAt,
        });

    public static IEnumerable<Weight> Weights(JsonElement root, DateTimeOffset fetchedAt)
    {
        foreach (var (item, date) in Dated(root, "calendarDate").Select(x => (x.item, x.date)))
        {
            var ts = WeightTimestamp(item);
            if (ts is null) continue;
            yield return new Weight
            {
                TimestampUtc = ts.Value,
                CalendarDate = date,
                WeightKg = JsonRead.Double(item, "weight"),
                Bmi = JsonRead.Double(item, "bmi"),
                BodyFatPct = JsonRead.Double(item, "bodyFat"),
                BodyWaterPct = JsonRead.Double(item, "bodyWater"),
                MuscleMass = JsonRead.Double(item, "muscleMass"),
                BoneMass = JsonRead.Double(item, "boneMass"),
                Raw = JsonRead.Clone(item),
                FetchedAt = fetchedAt,
            };
        }
    }

    public static HeartRateDaily? HeartRateDay(JsonElement root, DateTimeOffset fetchedAt)
    {
        var date = JsonRead.Date(root, "calendarDate");
        if (date is null) return null;
        return new HeartRateDaily
        {
            CalendarDate = date.Value,
            Resting = JsonRead.Int(root, "restingHeartRate"),
            Min = JsonRead.Int(root, "minHeartRate"),
            Max = JsonRead.Int(root, "maxHeartRate"),
            SevenDayAvgResting = JsonRead.Int(root, "lastSevenDaysAvgRestingHeartRate"),
            Raw = JsonRead.Clone(root),
            FetchedAt = fetchedAt,
        };
    }

    /// <summary>Items sharing a <c>calendarDate</c> become one row whose Raw is an array of those items.</summary>
    public static IEnumerable<RawDaily> GroupByCalendarDate(string dataset, JsonElement arrayRoot, DateTimeOffset fetchedAt)
    {
        var groups = new Dictionary<DateOnly, List<JsonElement>>();
        foreach (var (item, date) in Dated(arrayRoot, "calendarDate").Select(x => (x.item, x.date)))
        {
            if (!groups.TryGetValue(date, out var list)) groups[date] = list = [];
            list.Add(item);
        }
        foreach (var (date, list) in groups.OrderBy(g => g.Key))
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms))
            {
                w.WriteStartArray();
                foreach (var e in list) e.WriteTo(w);
                w.WriteEndArray();
            }
            yield return new RawDaily
            {
                Dataset = dataset,
                CalendarDate = date,
                Raw = JsonDocument.Parse(ms.ToArray()),
                FetchedAt = fetchedAt,
            };
        }
    }

    public static RawDaily RawDay(string dataset, DateOnly date, JsonElement root, DateTimeOffset fetchedAt) =>
        new() { Dataset = dataset, CalendarDate = date, Raw = JsonRead.Clone(root), FetchedAt = fetchedAt };

    private static IEnumerable<JsonElement> Items(JsonElement? array) =>
        array is { ValueKind: JsonValueKind.Array } a
            ? a.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.Object)
            : [];

    private static IEnumerable<(JsonElement item, DateOnly date)> Dated(JsonElement array, string dateProp) =>
        Items(array.ValueKind == JsonValueKind.Undefined ? null : array)
            .Select(i => (item: i, date: JsonRead.Date(i, dateProp)))
            .Where(x => x.date is not null)
            .Select(x => (x.item, x.date!.Value));

    private static DateTime? LocalTime(JsonElement item, string prop)
    {
        var v = JsonRead.Path(item, prop);
        if (v is null) return null;
        if (v.Value.ValueKind == JsonValueKind.String)
            return GarminTime.TryParseLocal(v.Value.GetString(), out var d) ? d : null;
        if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetInt64(out var ms))
            return DateTime.SpecifyKind(DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime, DateTimeKind.Unspecified);
        return null;
    }

    private static DateTimeOffset? WeightTimestamp(JsonElement item)
    {
        var v = JsonRead.Path(item, "timestampGMT");
        if (v is null) return null;
        if (v.Value.ValueKind == JsonValueKind.String)
            return GarminTime.TryParseUtc(v.Value.GetString(), out var t) ? t : null;
        if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetInt64(out var ms))
            return DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return null;
    }

    private static (int? min, int? max) BodyBatteryRange(JsonElement item)
    {
        var col = -1;
        foreach (var d in Items(JsonRead.Path(item, "bodyBatteryValueDescriptorDTOList")))
        {
            if (JsonRead.String(d, "bodyBatteryValueDescriptorKey") == "bodyBatteryLevel")
            {
                col = JsonRead.Int(d, "bodyBatteryValueDescriptorIndex") ?? -1;
                break;
            }
        }
        if (col < 0 || JsonRead.Path(item, "bodyBatteryValuesArray") is not { ValueKind: JsonValueKind.Array } rows)
            return (null, null);

        int? min = null, max = null;
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() <= col) continue;
            var cell = row[col];
            if (cell.ValueKind != JsonValueKind.Number || !cell.TryGetDouble(out var d)) continue;
            var v = (int)Math.Round(d, MidpointRounding.AwayFromZero);
            min = min is null ? v : Math.Min(min.Value, v);
            max = max is null ? v : Math.Max(max.Value, v);
        }
        return (min, max);
    }
}
