using System.Globalization;

namespace GarminMcp.Mapping;

public static class GarminTime
{
    private const DateTimeStyles UtcStyles =
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;

    /// <summary>Parses a Garmin timestamp; values without a zone are UTC. Offset is always zero.</summary>
    public static DateTimeOffset ParseUtc(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, UtcStyles);

    public static bool TryParseUtc(string? s, out DateTimeOffset value)
    {
        value = default;
        return s is not null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, UtcStyles, out value);
    }

    /// <summary>Parses a local (zoneless) Garmin timestamp; the clock time is kept as written, Kind = Unspecified.</summary>
    public static DateTime ParseLocal(string s) =>
        DateTime.SpecifyKind(
            DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal).DateTime,
            DateTimeKind.Unspecified);

    public static bool TryParseLocal(string? s, out DateTime value)
    {
        value = default;
        if (s is null
            || !DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            return false;
        value = DateTime.SpecifyKind(dto.DateTime, DateTimeKind.Unspecified);
        return true;
    }
}
