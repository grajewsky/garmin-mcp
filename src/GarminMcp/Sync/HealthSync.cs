using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using GarminMcp.Garmin;
using GarminMcp.Mapping;
using GarminMcp.Storage;
using GarminMcp.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GarminMcp.Sync;

/// <summary>Synchronizacja danych zdrowotnych w zakotwiczonych chunkach (finalne chunki są pomijane, ogon odświeżany) i snapshotów.</summary>
public sealed class HealthSync(GarminApiClient api, GarminDbContext db, IOptions<GarminOptions> options, TimeProvider time)
{
    /// <summary>Po tylu kolejnych nieudanych chunkach zbiór jest pomijany do końca tego przebiegu.</summary>
    public const int MaxConsecutiveFailures = 5;

    public async Task RunAsync(SyncScope scope, DateOnly historyStart, ISyncProgress progress, CancellationToken ct,
        IReadOnlyList<HealthDataset>? datasets = null)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var tail = options.Value.TailRefreshDays;

        foreach (var ds in datasets ?? HealthDatasets.All)
        {
            var recorded = await db.HealthChunks.AsNoTracking().Where(c => c.Dataset == ds.Name)
                .ToDictionaryAsync(c => c.ChunkStart, c => c.FetchedAt, ct);
            var chunks = DateChunker.Chunk(historyStart, scope.From ?? historyStart, Min(scope.To ?? today, today), ds.ChunkDays)
                .Where(c => DateChunker.NeedsFetch(c.NominalEnd, recorded.TryGetValue(c.Start, out var at) ? at : null, scope.Force, tail))
                .ToList();

            progress.SetPhase($"health:{ds.Name}", chunks.Count);
            var failures = 0;
            foreach (var chunk in chunks)
            {
                try
                {
                    await FetchChunkAsync(ds, chunk, today, ct);
                    failures = 0;
                }
                catch (Exception ex) when (ex is not (GarminAuthException or OperationCanceledException or DbException or DbUpdateException))
                {
                    var error = Describe(ex);
                    progress.AddError($"{ds.Name} {Fmt(chunk.Start)}: {error}");
                    if (++failures >= MaxConsecutiveFailures)
                    {
                        progress.AddError($"{ds.Name}: stopped after {MaxConsecutiveFailures} consecutive failures (last: {error})");
                        break;
                    }
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
                progress.Advance();
            }
        }

        await SnapshotsAsync(historyStart, today, progress, ct);
    }

    private async Task FetchChunkAsync(HealthDataset ds, ChunkSpan chunk, DateOnly today, CancellationToken ct)
    {
        using var doc = await api.GetJsonAsync(ds.Path(chunk.Start, Min(chunk.NominalEnd, today)), ct);
        var now = time.GetUtcNow();
        if (doc is not null)
        {
            var result = await ds.UpsertAsync(db, doc.RootElement, chunk.Start, now);
            if (result.Mapped < result.Items)
            {
                // Kształt odpowiedzi niezgodny z mapperem: zapisz to, co się dało, ale nie oznaczaj chunka jako pobranego.
                await db.SaveChangesAsync(ct);
                throw new SyncException($"{result.Items} items, {result.Mapped} mapped");
            }
        }

        var row = await db.HealthChunks.FindAsync([ds.Name, chunk.Start], ct);
        if (row is null)
            db.HealthChunks.Add(new HealthChunk { Dataset = ds.Name, ChunkStart = chunk.Start, ChunkEnd = chunk.NominalEnd, FetchedAt = now });
        else
        {
            row.ChunkEnd = chunk.NominalEnd;
            row.FetchedAt = now;
        }
        await db.SaveChangesAsync(ct);
    }

    private async Task SnapshotsAsync(DateOnly historyStart, DateOnly today, ISyncProgress progress, CancellationToken ct)
    {
        (string Name, string Path)[] snapshots =
        [
            ("hr_zones", "/biometric-service/heartRateZones/v2"),
            ("user_profile", "/userprofile-service/userprofile/settings"),
            ("fitness_age", $"/fitnessage-service/fitnessage/v2/{Fmt(today)}"),
            ("weight_goals", $"/goal-service/goal/user/effective/weightgoal/{Fmt(historyStart)}/{Fmt(today)}"),
        ];

        progress.SetPhase("health:snapshots", snapshots.Length);
        foreach (var (name, path) in snapshots)
        {
            try
            {
                using var doc = await api.GetJsonAsync(path, ct);
                if (doc is not null)
                {
                    db.Snapshots.Add(new Snapshot { Dataset = name, FetchedAt = time.GetUtcNow(), Raw = JsonRead.Clone(doc.RootElement) });
                    await db.SaveChangesAsync(ct);
                }
            }
            catch (Exception ex) when (ex is GarminApiException or JsonException)
            {
                progress.AddError($"snapshot {name}: {Describe(ex)}");
            }
            finally
            {
                db.ChangeTracker.Clear();
            }
            progress.Advance();
        }
    }

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static string Describe(Exception ex) => ex is GarminApiException api ? $"HTTP {api.StatusCode}" : ex.Message;

    private static string Fmt(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
