using System.Globalization;
using System.Text.Json;

namespace GarminMcp.Mapping;

/// <summary>Tolerant JSON readers: missing/null/wrong kind yields null, never throws.</summary>
internal static class JsonRead
{
    public static JsonElement? Path(JsonElement e, params string[] path)
    {
        var cur = e;
        foreach (var p in path)
        {
            if (cur.ValueKind != JsonValueKind.Object || !cur.TryGetProperty(p, out cur))
                return null;
        }
        return cur.ValueKind == JsonValueKind.Null || cur.ValueKind == JsonValueKind.Undefined ? null : cur;
    }

    public static double? Double(JsonElement e, params string[] path)
    {
        var v = Path(e, path);
        if (v is null) return null;
        if (v.Value.ValueKind == JsonValueKind.Number && v.Value.TryGetDouble(out var d)) return d;
        return null;
    }

    public static int? Int(JsonElement e, params string[] path)
    {
        var d = Double(e, path);
        if (d is null || double.IsNaN(d.Value)) return null;
        var r = Math.Round(d.Value, MidpointRounding.AwayFromZero);
        return r is >= int.MinValue and <= int.MaxValue ? (int)r : null;
    }

    public static string? String(JsonElement e, params string[] path)
    {
        var v = Path(e, path);
        return v is { ValueKind: JsonValueKind.String } ? v.Value.GetString() : null;
    }

    public static DateOnly? Date(JsonElement e, params string[] path)
    {
        var s = String(e, path);
        if (s is null) return null;
        var head = s.Length >= 10 ? s[..10] : s;
        return DateOnly.TryParseExact(head, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;
    }

    public static JsonDocument Clone(JsonElement e) => JsonDocument.Parse(e.GetRawText());
}
