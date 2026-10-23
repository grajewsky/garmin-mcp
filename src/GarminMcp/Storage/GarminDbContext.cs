using GarminMcp.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace GarminMcp.Storage;

public class GarminDbContext(DbContextOptions<GarminDbContext> options) : DbContext(options)
{
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<ActivityFile> ActivityFiles => Set<ActivityFile>();
    public DbSet<ActivityLap> ActivityLaps => Set<ActivityLap>();
    public DbSet<ActivityRecord> ActivityRecords => Set<ActivityRecord>();
    public DbSet<DailySummary> DailySummaries => Set<DailySummary>();
    public DbSet<Sleep> Sleeps => Set<Sleep>();
    public DbSet<Hrv> Hrvs => Set<Hrv>();
    public DbSet<BodyBattery> BodyBatteries => Set<BodyBattery>();
    public DbSet<Stress> Stresses => Set<Stress>();
    public DbSet<StepsDay> Steps => Set<StepsDay>();
    public DbSet<Hydration> Hydrations => Set<Hydration>();
    public DbSet<Weight> Weights => Set<Weight>();
    public DbSet<HeartRateDaily> HeartRateDailies => Set<HeartRateDaily>();
    public DbSet<RawDaily> RawDailies => Set<RawDaily>();
    public DbSet<Snapshot> Snapshots => Set<Snapshot>();
    public DbSet<HealthChunk> HealthChunks => Set<HealthChunk>();
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Activity>(e =>
        {
            e.ToTable("activities");
            e.HasKey(x => x.ActivityId);
            e.Property(x => x.ActivityId).ValueGeneratedNever();
            e.Property(x => x.StartTimeLocal).HasColumnType("timestamp without time zone");
            e.Property(x => x.Raw).HasColumnType("jsonb");
            e.Property(x => x.RawDetail).HasColumnType("jsonb");
            e.Property(x => x.RawWorkouts).HasColumnType("jsonb");
            e.Property(x => x.FileStatus).HasDefaultValue("pending");
            e.HasIndex(x => x.StartTimeLocal);
        });
        b.Entity<ActivityFile>(e =>
        {
            e.ToTable("activity_files");
            e.HasKey(x => x.ActivityId);
            e.Property(x => x.ActivityId).ValueGeneratedNever();
        });
        b.Entity<ActivityLap>(e =>
        {
            e.ToTable("activity_laps");
            e.HasKey(x => new { x.ActivityId, x.LapIndex });
        });
        b.Entity<ActivityRecord>(e =>
        {
            e.ToTable("activity_records");
            e.HasKey(x => new { x.ActivityId, x.TimestampUtc });
        });

        Daily<DailySummary>(b, "daily_summary");
        Daily<Sleep>(b, "sleep", e =>
        {
            e.Property(x => x.SleepStartLocal).HasColumnType("timestamp without time zone");
            e.Property(x => x.SleepEndLocal).HasColumnType("timestamp without time zone");
        });
        Daily<Hrv>(b, "hrv");
        Daily<BodyBattery>(b, "body_battery");
        Daily<Stress>(b, "stress");
        Daily<StepsDay>(b, "steps");
        Daily<Hydration>(b, "hydration");
        Daily<HeartRateDaily>(b, "heart_rate_daily");

        b.Entity<Weight>(e =>
        {
            e.ToTable("weight");
            e.HasKey(x => x.TimestampUtc);
            e.Property(x => x.TimestampUtc).ValueGeneratedNever();
            e.Property(x => x.Raw).HasColumnType("jsonb");
        });
        b.Entity<RawDaily>(e =>
        {
            e.ToTable("raw_daily");
            e.HasKey(x => new { x.Dataset, x.CalendarDate });
            e.Property(x => x.Raw).HasColumnType("jsonb");
        });
        b.Entity<Snapshot>(e =>
        {
            e.ToTable("snapshots");
            e.Property(x => x.Raw).HasColumnType("jsonb");
            e.HasIndex(x => new { x.Dataset, x.FetchedAt });
        });
        b.Entity<HealthChunk>(e =>
        {
            e.ToTable("health_chunks");
            e.HasKey(x => new { x.Dataset, x.ChunkStart });
        });
        b.Entity<SyncJob>(e =>
        {
            e.ToTable("sync_jobs");
            e.Property(x => x.Errors).HasColumnType("jsonb");
        });
        b.Entity<AuthToken>(e =>
        {
            e.ToTable("auth_token");
            e.Property(x => x.Id).ValueGeneratedNever();
        });
    }

    private static void Daily<T>(ModelBuilder b, string table, Action<Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T>>? extra = null)
        where T : class
    {
        b.Entity<T>(e =>
        {
            e.ToTable(table);
            e.HasKey("CalendarDate");
            e.Property("CalendarDate").ValueGeneratedNever();
            e.Property("Raw").HasColumnType("jsonb");
            extra?.Invoke(e);
        });
    }
}
