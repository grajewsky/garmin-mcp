using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GarminMcp.Tools;

/// <summary>Wspólna serializacja odpowiedzi narzędzi MCP (snake_case, bez pól null).</summary>
public static class ToolJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new UtcSecondsConverter() },
    };

    /// <summary>Format znaczników UTC w odpowiedziach narzędzi.</summary>
    public const string UtcFormat = "yyyy-MM-ddTHH:mm:ss'Z'";

    public static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString(UtcFormat, CultureInfo.InvariantCulture);

    public static string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);

    public static string Error(string message) => Serialize(new { Error = message });

    public static string NoData(string range) => Serialize(new { Message = NoDataMessage(range) });

    public static string NoDataMessage(string range) => $"No data for {range}. Run the sync tool to fetch data from Garmin.";

    /// <summary>Pusty/null → <c>true</c> z <c>date = null</c>; poprawne <c>YYYY-MM-DD</c> → <c>true</c>; inaczej <c>false</c>.</summary>
    public static bool TryParseDate(string? s, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(s)) return true;
        if (DateOnly.TryParseExact(s.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            date = d;
            return true;
        }
        return false;
    }

    /// <summary>Każdy <see cref="DateTimeOffset"/> w odpowiedzi jako UTC z dokładnością do sekundy: <c>yyyy-MM-ddTHH:mm:ssZ</c>.</summary>
    private sealed class UtcSecondsConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
            writer.WriteStringValue(Utc(value));
    }
}
