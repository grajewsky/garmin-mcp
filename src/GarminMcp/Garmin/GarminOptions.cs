namespace GarminMcp.Garmin;

/// <summary>Konfiguracja klienta Garmin (sekcja <c>Garmin</c>).</summary>
public sealed class GarminOptions
{
    public string BaseUrl { get; set; } = "https://personal.connectapi.garmin.com";
    public string ApiKeyFile { get; set; } = "/run/secrets/garmin_api_key";
    public TimeSpan RequestDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    public int MaxRetries { get; set; } = 3;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);
    public int TailRefreshDays { get; set; } = 7;
}
