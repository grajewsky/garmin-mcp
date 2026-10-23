using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GarminMcp.Mapping;
using GarminMcp.Storage;
using GarminMcp.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace GarminMcp.Sync;

/// <summary>
/// Definicja zbioru danych zdrowotnych. <c>UpsertAsync(db, root, chunkStart, fetchedAt)</c> mapuje odpowiedź
/// i upsertuje wiersze po kluczu (istniejący wiersz → nadpisanie kolumn, <c>Raw</c> i <c>FetchedAt</c>); nie zapisuje zmian.
/// </summary>
public sealed record HealthDataset(
    string Name,
    int ChunkDays,
    Func<DateOnly, DateOnly, string> Path,
    Func<GarminDbContext, JsonElement, DateOnly, DateTimeOffset, Task> UpsertAsync);

public static class HealthDatasets
{
    public static IReadOnlyList<HealthDataset> All { get; } =
    [
        Range("daily_summary", 365, (s, e) => $"/usersummary-service/v2/dailysummaries?startDate={F(s)}&endDate={F(e)}",
            JsonValueKind.Object, (db, r, at) => UpsertAsync(db, HealthMappers.DailySummaries(r, at), x => [x.CalendarDate])),
        Range("sleep", 31, (s, e) => $"/sleep-service/sleep/dailySleepSummariesByDate?startDate={F(s)}&endDate={F(e)}",
            JsonValueKind.Array, (db, r, at) => UpsertAsync(db, HealthMappers.Sleeps(r, at), x => [x.CalendarDate])),
        Range("hrv", 31, (s, e) => $"/hrv-service/hrv/daily/{F(s)}/{F(e)}",
            JsonValueKind.Object, (db, r, at) => UpsertAsync(db, HealthMappers.Hrvs(r, at), x => [x.CalendarDate])),
        Range("body_battery", 31, (s, e) => $"/wellness-service/wellness/bodyBattery/reports/daily?startDate={F(s)}&endDate={F(e)}",
            JsonValueKind.Array, (db, r, at) => UpsertAsync(db, HealthMappers.BodyBatteries(r, at), x => [x.CalendarDate])),
        Range("stress", 28, (s, e) => $"/usersummary-service/stats/stress/daily/{F(s)}/{F(e)}",
            JsonValueKind.Array, (db, r, at) => UpsertAsync(db, HealthMappers.Stresses(r, at), x => [x.CalendarDate])),
        Range("steps", 28, (s, e) => $"/usersummary-service/stats/steps/daily/{F(s)}/{F(e)}",
            JsonValueKind.Array, (db, r, at) => UpsertAsync(db, HealthMappers.Steps(r, at), x => [x.CalendarDate])),
        Range("hydration", 31, (s, e) => $"/usersummary-service/stats/hydration/daily/{F(s)}/{F(e)}",
            JsonValueKind.Array, (db, r, at) => UpsertAsync(db, HealthMappers.Hydrations(r, at), x => [x.CalendarDate])),
        Range("weight", 31, (s, e) => $"/weight-service/weight/all/{F(s)}/{F(e)}",
            JsonValueKind.Array, (db, r, at) => UpsertAsync(db, HealthMappers.Weights(r, at), x => [x.TimestampUtc])),
        Range("wellness_activities", 31, (s, e) => $"/wellnessactivity-service/activity/summary/range/{F(s)}/{F(e)}",
            JsonValueKind.Array, (db, r, at) => UpsertAsync(db, HealthMappers.GroupByCalendarDate("wellness_activities", r, at), x => [x.Dataset, x.CalendarDate])),
        Day("heart_rate_daily", s => $"/wellness-service/wellness/dailyHeartRate?date={F(s)}",
            (db, r, start, at) => UpsertAsync(db, [HeartRateFor(r, start, at)], x => [x.CalendarDate]),
            JsonValueKind.Object),
        RawDay("spo2", s => $"/wellness-service/wellness/daily/spo2acclimation/{F(s)}"),
        RawDay("nutrition", s => $"/nutrition-service/food/logs/{F(s)}"),
        RawDay("menstrual_summary", s => $"/periodichealth-service/menstrualcycle/summary/{F(s)}"),
        RawDay("menstrual_dayview", s => $"/periodichealth-service/menstrualcycle/dayview/{F(s)}"),
    ];

    /// <summary>Wiersz tętna dla dnia chunka (klucz = data żądania, niezależnie od calendarDate w odpowiedzi).</summary>
    private static HeartRateDaily HeartRateFor(JsonElement root, DateOnly start, DateTimeOffset at)
    {
        var row = HealthMappers.HeartRateDay(root, at);
        if (row is null)
        {
            var node = JsonNode.Parse(root.GetRawText())!;
            node["calendarDate"] = F(start);
            using var patched = JsonDocument.Parse(node.ToJsonString());
            row = HealthMappers.HeartRateDay(patched.RootElement, at)!;
            row.Raw = JsonRead.Clone(root);
        }
        row.CalendarDate = start;
        return row;
    }

    private static string F(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static HealthDataset Range(string name, int days, Func<DateOnly, DateOnly, string> path, JsonValueKind kind,
        Func<GarminDbContext, JsonElement, DateTimeOffset, Task> upsert) =>
        new(name, days, path, (db, root, _, at) =>
        {
            Require(name, root, kind);
            return upsert(db, root, at);
        });

    private static HealthDataset Day(string name, Func<DateOnly, string> path,
        Func<GarminDbContext, JsonElement, DateOnly, DateTimeOffset, Task> upsert, JsonValueKind kind) =>
        new(name, 1, (s, _) => path(s), (db, root, start, at) =>
        {
            Require(name, root, kind);
            return upsert(db, root, start, at);
        });

    private static HealthDataset RawDay(string name, Func<DateOnly, string> path) =>
        new(name, 1, (s, _) => path(s), (db, root, start, at) =>
            UpsertAsync(db, [HealthMappers.RawDay(name, start, root, at)], x => [x.Dataset, x.CalendarDate]));

    private static void Require(string name, JsonElement root, JsonValueKind kind)
    {
        if (root.ValueKind != kind)
            throw new JsonException($"{name}: expected JSON {kind} but got {root.ValueKind}");
    }

    /// <summary>Upsert po kluczu: istniejący wiersz dostaje wszystkie kolumny z nowego, w przeciwnym razie dodanie.</summary>
    private static async Task UpsertAsync<T>(GarminDbContext db, IEnumerable<T> rows, Func<T, object?[]> key) where T : class
    {
        foreach (var row in rows)
        {
            var existing = await db.Set<T>().FindAsync(key(row)!);
            if (existing is null) db.Set<T>().Add(row);
            else db.Entry(existing).CurrentValues.SetValues(row);
        }
    }
}
