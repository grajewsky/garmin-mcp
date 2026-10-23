using GarminMcp.Storage.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace GarminMcp.Storage;

/// <summary>Zastępuje rekordy aktywności: DELETE + binary COPY w jednej transakcji.</summary>
public sealed class ActivityRecordWriter(GarminDbContext db)
{
    private const string CopySql =
        "COPY activity_records (activity_id, timestamp_utc, elapsed_s, hr, speed_mps, cadence, altitude_m, power_w, distance_m, lat, lon) FROM STDIN (FORMAT BINARY)";

    public async Task ReplaceAsync(long activityId, IReadOnlyList<ActivityRecord> records, CancellationToken ct)
    {
        var own = db.Database.CurrentTransaction is null;
        var tx = own ? await db.Database.BeginTransactionAsync(ct) : db.Database.CurrentTransaction!;
        await db.ActivityRecords.Where(r => r.ActivityId == activityId).ExecuteDeleteAsync(ct);

        var conn = (NpgsqlConnection)db.Database.GetDbConnection();
        await using (var importer = await conn.BeginBinaryImportAsync(CopySql, ct))
        {
            foreach (var r in records)
            {
                await importer.StartRowAsync(ct);
                await importer.WriteAsync(activityId, NpgsqlDbType.Bigint, ct);
                await importer.WriteAsync(r.TimestampUtc.UtcDateTime, NpgsqlDbType.TimestampTz, ct);
                await importer.WriteAsync(r.ElapsedS, NpgsqlDbType.Integer, ct);
                await WriteNullable(importer, r.Hr, NpgsqlDbType.Smallint, ct);
                await WriteNullable(importer, r.SpeedMps, NpgsqlDbType.Real, ct);
                await WriteNullable(importer, r.Cadence, NpgsqlDbType.Smallint, ct);
                await WriteNullable(importer, r.AltitudeM, NpgsqlDbType.Real, ct);
                await WriteNullable(importer, r.PowerW, NpgsqlDbType.Smallint, ct);
                await WriteNullable(importer, r.DistanceM, NpgsqlDbType.Real, ct);
                await WriteNullable(importer, r.Lat, NpgsqlDbType.Double, ct);
                await WriteNullable(importer, r.Lon, NpgsqlDbType.Double, ct);
            }
            await importer.CompleteAsync(ct);
        }

        if (own)
        {
            await tx.CommitAsync(ct);
            await tx.DisposeAsync();
        }
    }

    private static async Task WriteNullable<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type, CancellationToken ct)
        where T : struct
    {
        if (value is { } v) await importer.WriteAsync(v, type, ct);
        else await importer.WriteNullAsync(ct);
    }
}
