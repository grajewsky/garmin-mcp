using System.IO.Compression;
using Dynastream.Fit;
using GarminMcp.Storage.Entities;

namespace GarminMcp.Fit;

public sealed record FitData(string Format, IReadOnlyList<ActivityRecord> Records, IReadOnlyList<ActivityLap> Laps);

public sealed class FitParseException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Parsuje pobrany z Garmina plik aktywności (zip z .fit albo surowy FIT) do rekordów i okrążeń.</summary>
public static class FitParser
{
    private const double SemicirclesToDegrees = 180.0 / 2147483648.0;

    public static FitData Parse(long activityId, byte[] downloaded)
    {
        if (downloaded.Length >= 2 && downloaded[0] == 'P' && downloaded[1] == 'K')
            return ParseZip(activityId, downloaded);
        if (IsFit(downloaded))
            return ParseFit(activityId, downloaded);
        return Empty("unknown");
    }

    private static FitData ParseZip(long activityId, byte[] zipBytes)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            return Empty("unknown");
        }

        using (zip)
        {
            var fit = FindEntry(zip, ".fit");
            if (fit is not null)
            {
                using var es = fit.Open();
                using var ms = new MemoryStream();
                es.CopyTo(ms);
                return ParseFit(activityId, ms.ToArray());
            }
            if (FindEntry(zip, ".gpx") is not null) return Empty("gpx");
            if (FindEntry(zip, ".tcx") is not null) return Empty("tcx");
            return Empty("unknown");
        }
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive zip, string extension) =>
        zip.Entries.FirstOrDefault(e => e.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static bool IsFit(byte[] data) =>
        data.Length >= 12 && data[8] == '.' && data[9] == 'F' && data[10] == 'I' && data[11] == 'T';

    private static FitData ParseFit(long activityId, byte[] fit)
    {
        var records = new List<ActivityRecord>();
        var laps = new List<ActivityLap>();
        var seen = new HashSet<DateTimeOffset>();
        DateTimeOffset? first = null;

        var broadcaster = new MesgBroadcaster();
        broadcaster.RecordMesgEvent += (_, e) =>
        {
            var m = (RecordMesg)e.mesg;
            var ts = m.GetTimestamp();
            if (ts is null) return;
            var t = ToUtc(ts);
            if (!seen.Add(t)) return;
            first ??= t;
            var speed = m.GetEnhancedSpeed() ?? m.GetSpeed();
            var alt = m.GetEnhancedAltitude() ?? m.GetAltitude();
            records.Add(new ActivityRecord
            {
                ActivityId = activityId,
                TimestampUtc = t,
                ElapsedS = (int)(t - first.Value).TotalSeconds,
                Hr = m.GetHeartRate(),
                SpeedMps = speed,
                Cadence = m.GetCadence(),
                AltitudeM = alt,
                PowerW = m.GetPower() is { } p ? (short)p : null,
                DistanceM = m.GetDistance(),
                Lat = m.GetPositionLat() is { } lat ? lat * SemicirclesToDegrees : null,
                Lon = m.GetPositionLong() is { } lon ? lon * SemicirclesToDegrees : null,
            });
        };
        broadcaster.LapMesgEvent += (_, e) =>
        {
            var m = (LapMesg)e.mesg;
            var start = m.GetStartTime();
            if (start is null) return;
            laps.Add(new ActivityLap
            {
                ActivityId = activityId,
                LapIndex = laps.Count,
                StartTimeUtc = ToUtc(start),
                ElapsedS = m.GetTotalElapsedTime(),
                DistanceM = m.GetTotalDistance(),
                AvgHr = m.GetAvgHeartRate(),
                MaxHr = m.GetMaxHeartRate(),
                AvgSpeedMps = m.GetEnhancedAvgSpeed() ?? m.GetAvgSpeed(),
                AvgCadence = m.GetAvgCadence(),
                AscentM = m.GetTotalAscent(),
            });
        };

        try
        {
            var decode = new Decode();
            decode.MesgEvent += broadcaster.OnMesg;
            using var stream = new MemoryStream(fit);
            if (!decode.CheckIntegrity(stream))
                throw new FitParseException("FIT file failed integrity check (truncated or CRC mismatch).");
            stream.Position = 0;
            decode.Read(stream);
        }
        catch (FitException ex)
        {
            throw new FitParseException($"Cannot decode FIT file: {ex.Message}", ex);
        }

        return new FitData("fit", records, laps);
    }

    private static DateTimeOffset ToUtc(Dynastream.Fit.DateTime t) =>
        new(System.DateTime.SpecifyKind(t.GetDateTime(), DateTimeKind.Utc), TimeSpan.Zero);

    private static FitData Empty(string format) => new(format, [], []);
}
