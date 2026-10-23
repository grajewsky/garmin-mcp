using System.IO.Compression;
using Dynastream.Fit;
using DateTime = System.DateTime;

namespace GarminMcp.Tests.Support;

/// <summary>Buduje syntetyczne pliki FIT (przez Encode z Garmin FIT SDK) do testów parsera.</summary>
public static class FitFileBuilder
{
    public static byte[] Build(
        DateTimeOffset start,
        int seconds,
        Func<int, (byte hr, float speed, int latSemi, int lonSemi)> sample,
        int lapEverySeconds)
    {
        using var stream = new MemoryStream();
        var encode = new Encode(ProtocolVersion.V20);
        encode.Open(stream);

        var fileId = new FileIdMesg();
        fileId.SetType(Dynastream.Fit.File.Activity);
        fileId.SetManufacturer(Manufacturer.Garmin);
        fileId.SetProduct(1);
        fileId.SetSerialNumber(12345);
        fileId.SetTimeCreated(ToFit(start));
        encode.Write(fileId);

        for (var i = 0; i < seconds; i++)
        {
            var (hr, speed, latSemi, lonSemi) = sample(i);
            var record = new RecordMesg();
            record.SetTimestamp(ToFit(start.AddSeconds(i)));
            record.SetHeartRate(hr);
            record.SetEnhancedSpeed(speed);
            record.SetDistance(speed * i);
            record.SetPositionLat(latSemi);
            record.SetPositionLong(lonSemi);
            encode.Write(record);

            if (lapEverySeconds > 0 && (i + 1) % lapEverySeconds == 0)
            {
                var lap = new LapMesg();
                lap.SetStartTime(ToFit(start.AddSeconds(i + 1 - lapEverySeconds)));
                lap.SetTimestamp(ToFit(start.AddSeconds(i)));
                lap.SetTotalElapsedTime(lapEverySeconds);
                lap.SetTotalDistance(speed * lapEverySeconds);
                lap.SetAvgHeartRate(hr);
                lap.SetMaxHeartRate(hr);
                lap.SetEnhancedAvgSpeed(speed);
                encode.Write(lap);
            }
        }

        var session = new SessionMesg();
        session.SetStartTime(ToFit(start));
        session.SetTimestamp(ToFit(start.AddSeconds(seconds)));
        session.SetTotalElapsedTime(seconds);
        encode.Write(session);

        var activity = new ActivityMesg();
        activity.SetTimestamp(ToFit(start.AddSeconds(seconds)));
        activity.SetNumSessions(1);
        encode.Write(activity);

        encode.Close();
        return stream.ToArray();
    }

    public static byte[] Zip(string entryName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var es = entry.Open();
            es.Write(content);
        }
        return ms.ToArray();
    }

    private static Dynastream.Fit.DateTime ToFit(DateTimeOffset t) => new(t.UtcDateTime);
}
