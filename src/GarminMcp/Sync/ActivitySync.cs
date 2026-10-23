using System.Globalization;
using System.Text.Json;
using GarminMcp.Fit;
using GarminMcp.Garmin;
using GarminMcp.Mapping;
using GarminMcp.Storage;
using GarminMcp.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace GarminMcp.Sync;

/// <summary>Synchronizacja aktywności: lista → oznaczenie usuniętych → szczegóły, workouts i plik oryginalny z parsowaniem FIT.</summary>
public sealed class ActivitySync(GarminApiClient api, GarminDbContext db, ActivityRecordWriter records, TimeProvider time)
{
    private const int PageSize = 99;
    private const int MaxPages = 1000;

    public async Task RunAsync(SyncScope scope, ISyncProgress progress, CancellationToken ct)
    {
        var seen = await ListAsync(scope, progress, ct);
        if (!scope.Force) await MarkDeletionsAsync(seen, ct);
        db.ChangeTracker.Clear();
        await FetchDetailsAsync(scope, progress, ct);
    }

    private async Task<HashSet<long>> ListAsync(SyncScope scope, ISyncProgress progress, CancellationToken ct)
    {
        progress.SetPhase("activities_list", 0);
        var seen = new HashSet<long>();
        var range = "";
        if (scope.From is { } from) range += $"&startDate={Fmt(from)}";
        if (scope.To is { } to) range += $"&endDate={Fmt(to)}";

        for (var page = 0; ; page++)
        {
            if (page >= MaxPages)
                throw new SyncException($"Activity list exceeded {MaxPages} pages");
            var path = $"/activitylist-service/activities/search/activities?start={page * PageSize}&limit={PageSize}&sortOrder=asc{range}";
            using var doc = await api.GetJsonAsync(path, ct);
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) break;
            var items = doc.RootElement.EnumerateArray().ToList();
            if (items.Count == 0) break;

            var now = time.GetUtcNow();
            var newIds = 0;
            var pageItems = new List<(long Id, JsonElement Item)>();
            foreach (var item in items)
            {
                long id;
                try { id = ActivityMapper.GetId(item); }
                catch (FormatException ex) { progress.AddError($"activity list item skipped: {ex.Message}"); continue; }
                if (seen.Add(id)) newIds++;
                pageItems.Add((id, item));
            }

            if (items.Count >= PageSize && newIds == 0)
                throw new SyncException("Activity list paging returned a repeated page");

            var ids = pageItems.Select(x => x.Id).Distinct().ToList();
            var existing = await db.Activities.Where(a => ids.Contains(a.ActivityId)).ToDictionaryAsync(a => a.ActivityId, ct);
            foreach (var (id, item) in pageItems)
            {
                try
                {
                    ActivityMapper.ApplyListItem(new Activity(), item, now); // walidacja przed dotknięciem encji
                }
                catch (FormatException ex)
                {
                    progress.AddError($"activity {id}: list item skipped: {ex.Message}");
                    continue;
                }
                if (!existing.TryGetValue(id, out var entity))
                {
                    entity = new Activity();
                    existing[id] = entity;
                    ActivityMapper.ApplyListItem(entity, item, now);
                    db.Activities.Add(entity);
                    continue;
                }
                ActivityMapper.ApplyListItem(entity, item, now);
            }
            await db.SaveChangesAsync(ct);
            progress.Advance();

            if (items.Count < PageSize) break;
        }
        return seen;
    }

    private async Task MarkDeletionsAsync(HashSet<long> seen, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var all = await db.Activities.Select(a => new { a.ActivityId, a.DeletedAt }).ToListAsync(ct);
        var toDelete = all.Where(a => a.DeletedAt is null && !seen.Contains(a.ActivityId)).Select(a => a.ActivityId).ToList();
        var toRestore = all.Where(a => a.DeletedAt is not null && seen.Contains(a.ActivityId)).Select(a => a.ActivityId).ToList();
        if (toDelete.Count > 0)
            await db.Activities.Where(a => toDelete.Contains(a.ActivityId))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletedAt, now), ct);
        if (toRestore.Count > 0)
            await db.Activities.Where(a => toRestore.Contains(a.ActivityId))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DeletedAt, (DateTimeOffset?)null), ct);
    }

    private async Task FetchDetailsAsync(SyncScope scope, ISyncProgress progress, CancellationToken ct)
    {
        var query = db.Activities.Where(a => a.DeletedAt == null);
        if (scope.Force)
        {
            var from = scope.From?.ToDateTime(TimeOnly.MinValue);
            var toExclusive = scope.To?.AddDays(1).ToDateTime(TimeOnly.MinValue);
            query = query.Where(a => (from == null || a.StartTimeLocal >= from) && (toExclusive == null || a.StartTimeLocal < toExclusive));
        }
        else
        {
            query = query.Where(a => a.DetailFetchedAt == null);
        }
        var candidates = await query.OrderBy(a => a.StartTimeLocal).ThenBy(a => a.ActivityId)
            .Select(a => a.ActivityId).ToListAsync(ct);

        progress.SetPhase("activities_detail", candidates.Count);
        foreach (var id in candidates)
        {
            try
            {
                await FetchOneAsync(id, progress, ct);
            }
            catch (GarminApiException ex)
            {
                progress.AddError($"activity {id}: HTTP {ex.StatusCode}");
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
            progress.Advance();
        }
    }

    private async Task FetchOneAsync(long id, ISyncProgress progress, CancellationToken ct)
    {
        using var detail = await api.GetJsonAsync($"/activity-service/activity/{id}", ct);
        using var workouts = await api.GetJsonAsync($"/activity-service/activity/{id}/workouts", ct);
        var file = await api.GetBytesAsync($"/download-service/files/activity/{id}", ct);

        var now = time.GetUtcNow();
        var activity = await db.Activities.SingleAsync(a => a.ActivityId == id, ct);
        activity.RawDetail = detail is null ? null : JsonRead.Clone(detail.RootElement);
        activity.RawWorkouts = workouts is null ? null : JsonRead.Clone(workouts.RootElement);

        FitData? parsed = null;
        if (file is null)
        {
            activity.FileStatus = "missing";
        }
        else
        {
            try
            {
                parsed = FitParser.Parse(id, file);
                activity.FileStatus = parsed.Format == "fit" ? "ok" : "unparsed";
            }
            catch (Exception ex) when (ex is not (GarminAuthException or OperationCanceledException))
            {
                activity.FileStatus = "error";
                progress.AddError($"activity {id}: cannot parse file: {ex.Message}");
            }
        }

        // Zastąp dane pochodne od pliku (bez duplikatów przy ponownym pobraniu).
        await db.ActivityFiles.Where(f => f.ActivityId == id).ExecuteDeleteAsync(ct);
        await db.ActivityLaps.Where(l => l.ActivityId == id).ExecuteDeleteAsync(ct);
        if (file is not null)
        {
            db.ActivityFiles.Add(new ActivityFile
            {
                ActivityId = id,
                Format = parsed?.Format ?? "unknown",
                Content = file,
                SizeBytes = file.Length,
                FetchedAt = now,
            });
        }
        if (parsed is { Format: "fit" })
        {
            await records.ReplaceAsync(id, parsed.Records.ToList(), ct);
            db.ActivityLaps.AddRange(parsed.Laps);
        }
        else
        {
            await db.ActivityRecords.Where(r => r.ActivityId == id).ExecuteDeleteAsync(ct);
        }

        activity.DetailFetchedAt = now;
        await db.SaveChangesAsync(ct);
    }

    private static string Fmt(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
