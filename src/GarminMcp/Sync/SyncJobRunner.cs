using System.Text.Json;
using GarminMcp.Garmin;
using GarminMcp.Storage;
using GarminMcp.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GarminMcp.Sync;

public sealed record SyncJobView(
    long JobId, string Status, string? Phase, int ItemsDone, int ItemsTotal, IReadOnlyList<string> Errors,
    string? Message, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, DateTimeOffset? LastSuccessfulSyncAt, bool Started);

/// <summary>Uruchamia synchronizację w tle (jedno zadanie naraz w procesie) i zapisuje postęp w tabeli <c>sync_jobs</c>.</summary>
public sealed class SyncJobRunner(IServiceScopeFactory scopes, TimeProvider time, ILogger<SyncJobRunner> log)
{
    private const int MaxStoredErrors = 50;
    private const int ViewErrors = 10;
    private const int AdvanceFlushEvery = 10;

    private readonly SemaphoreSlim _startLock = new(1, 1);
    private long _currentJobId;
    private Task? _currentTask;

    /// <summary>Tylko testy: ogranicza zestawy danych zdrowotnych (produkcyjnie <c>null</c> = wszystkie).</summary>
    internal IReadOnlyList<HealthDataset>? HealthDatasetsOverride { get; set; }

    internal Task? CurrentTask => Volatile.Read(ref _currentTask);

    public async Task<SyncJobView> StartAsync(DateOnly? from, DateOnly? to)
    {
        await _startLock.WaitAsync();
        try
        {
            if (_currentTask is { IsCompleted: false })
            {
                var running = await ReadViewAsync(_currentJobId, started: false);
                if (running is { Status: "running" }) return running;
            }

            var scope = new SyncScope(from, to);
            SyncJob job;
            await using (var s = scopes.CreateAsyncScope())
            {
                var db = s.ServiceProvider.GetRequiredService<GarminDbContext>();
                job = new SyncJob
                {
                    Status = "running",
                    RangeFrom = from,
                    RangeTo = to,
                    Force = scope.Force,
                    StartedAt = time.GetUtcNow(),
                };
                db.SyncJobs.Add(job);
                await db.SaveChangesAsync();
            }

            _currentJobId = job.Id;
            _currentTask = Task.Run(() => RunJobAsync(job.Id, scope));
            return (await ReadViewAsync(job.Id, started: true))!;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<SyncJobView?> GetStatusAsync()
    {
        await using var s = scopes.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<GarminDbContext>();
        var id = await db.SyncJobs.AsNoTracking().OrderByDescending(j => j.Id).Select(j => (long?)j.Id).FirstOrDefaultAsync();
        return id is null ? null : await ReadViewAsync(id.Value, started: false);
    }

    public async Task MarkInterruptedAsync()
    {
        await using var s = scopes.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<GarminDbContext>();
        var now = time.GetUtcNow();
        await db.SyncJobs.Where(j => j.Status == "running").ExecuteUpdateAsync(u => u
            .SetProperty(j => j.Status, "interrupted")
            .SetProperty(j => j.Message, "Interrupted by restart; run sync again to resume.")
            .SetProperty(j => j.FinishedAt, now));
    }

    private async Task<SyncJobView?> ReadViewAsync(long id, bool started)
    {
        await using var s = scopes.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<GarminDbContext>();
        var job = await db.SyncJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Id == id);
        if (job is null) return null;
        var lastOk = await db.SyncJobs.AsNoTracking().Where(j => j.Status == "completed")
            .MaxAsync(j => j.FinishedAt);
        var errors = job.Errors.RootElement.EnumerateArray().Select(e => e.GetString() ?? "").ToList();
        return new SyncJobView(job.Id, job.Status, job.Phase, job.ItemsDone, job.ItemsTotal,
            errors.Skip(Math.Max(0, errors.Count - ViewErrors)).ToList(), job.Message, job.StartedAt, job.FinishedAt, lastOk, started);
    }

    private async Task RunJobAsync(long jobId, SyncScope scope)
    {
        string status = "failed";
        string? message = null;
        using var progress = new JobProgress(scopes, time, log, jobId);
        try
        {
            await using var s = scopes.CreateAsyncScope();
            var sp = s.ServiceProvider;
            await sp.GetRequiredService<ActivitySync>().RunAsync(scope, progress, CancellationToken.None);

            var db = sp.GetRequiredService<GarminDbContext>();
            var earliest = await db.Activities.Where(a => a.DeletedAt == null)
                .MinAsync(a => (DateTime?)a.StartTimeLocal);
            var historyStart = DateOnly.FromDateTime(earliest ?? time.GetUtcNow().UtcDateTime);
            await sp.GetRequiredService<HealthSync>()
                .RunAsync(scope, historyStart, progress, CancellationToken.None, HealthDatasetsOverride);
            status = "completed";
        }
        catch (GarminAuthException ex)
        {
            status = "auth_failed";
            message = ex.Message;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Sync job {JobId} failed", jobId);
            message = ex.Message;
        }

        try
        {
            progress.Finish(status, message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Could not record final state of sync job {JobId}", jobId);
        }
    }

    /// <summary>Zapisuje postęp na własnym DbContext; zapisy są szeregowane blokadą.</summary>
    private sealed class JobProgress : ISyncProgress, IDisposable
    {
        private readonly TimeProvider _time;
        private readonly ILogger _log;
        private readonly long _jobId;
        private readonly object _lock = new();
        private readonly IServiceScope _scope;
        private readonly GarminDbContext _db;
        private readonly List<string> _errors = [];
        private string? _phase;
        private int _done, _total, _sinceFlush;

        public JobProgress(IServiceScopeFactory scopes, TimeProvider time, ILogger log, long jobId)
        {
            _time = time;
            _log = log;
            _jobId = jobId;
            _scope = scopes.CreateScope();
            _db = _scope.ServiceProvider.GetRequiredService<GarminDbContext>();
        }

        public void SetPhase(string phase, int total)
        {
            lock (_lock)
            {
                _phase = phase;
                _total = total;
                _done = 0;
                _sinceFlush = 0;
                Flush();
            }
        }

        public void Advance(int n = 1)
        {
            lock (_lock)
            {
                _done += n;
                _sinceFlush += n;
                if (_sinceFlush >= AdvanceFlushEvery) Flush();
            }
        }

        public void AddError(string message)
        {
            lock (_lock)
            {
                _errors.Add(message);
                if (_errors.Count > MaxStoredErrors) _errors.RemoveRange(0, _errors.Count - MaxStoredErrors);
                Flush();
            }
        }

        public void Finish(string status, string? message)
        {
            lock (_lock)
            {
                var finished = _time.GetUtcNow();
                var errors = JsonSerializer.SerializeToDocument(_errors);
                _db.ChangeTracker.Clear();
                _db.SyncJobs.Where(j => j.Id == _jobId).ExecuteUpdate(u => u
                    .SetProperty(j => j.Status, status)
                    .SetProperty(j => j.Message, message)
                    .SetProperty(j => j.Phase, _phase)
                    .SetProperty(j => j.ItemsDone, _done)
                    .SetProperty(j => j.ItemsTotal, _total)
                    .SetProperty(j => j.Errors, errors)
                    .SetProperty(j => j.FinishedAt, finished));
            }
        }

        // Wywoływane pod blokadą; błąd zapisu postępu nie przerywa synchronizacji.
        private void Flush()
        {
            _sinceFlush = 0;
            try
            {
                var errors = JsonSerializer.SerializeToDocument(_errors);
                var phase = _phase;
                var done = _done;
                var total = _total;
                _db.SyncJobs.Where(j => j.Id == _jobId).ExecuteUpdate(u => u
                    .SetProperty(j => j.Phase, phase)
                    .SetProperty(j => j.ItemsDone, done)
                    .SetProperty(j => j.ItemsTotal, total)
                    .SetProperty(j => j.Errors, errors));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not persist progress of sync job {JobId}", _jobId);
            }
        }

        public void Dispose() => _scope.Dispose();
    }
}
