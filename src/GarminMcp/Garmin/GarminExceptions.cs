namespace GarminMcp.Garmin;

/// <summary>Błąd uwierzytelnienia. Komunikat nie zawiera klucza ani tokenu.</summary>
public sealed class GarminAuthException(string message) : Exception(message);

public sealed class GarminApiException(int statusCode, string path)
    : Exception($"Garmin API request to {path} failed with status {statusCode}.")
{
    public int StatusCode { get; } = statusCode;
    public string Path { get; } = path;
}
