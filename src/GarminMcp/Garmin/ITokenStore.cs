namespace GarminMcp.Garmin;

public sealed record StoredToken(string Token, DateTimeOffset ExpiresAt);

/// <summary>Trwały magazyn tokenu dostępowego.</summary>
public interface ITokenStore
{
    Task<StoredToken?> LoadAsync(CancellationToken ct);
    Task SaveAsync(StoredToken token, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}
