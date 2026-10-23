namespace GarminMcp.Garmin;

/// <summary>
/// Stan uwierzytelnienia w obrębie jednego zadania synchronizacji (scoped, współdzielony przez klientów zadania):
/// najwyżej jedno wymuszone odświeżenie tokenu na zadanie i token, który już zadziałał.
/// </summary>
public sealed class GarminAuthSession
{
    private readonly object _lock = new();
    private bool _refreshUsed;
    private string? _confirmedToken;

    /// <summary>Token zaakceptowany przez endpoint danych w tym zadaniu.</summary>
    public void Confirm(string token)
    {
        lock (_lock) _confirmedToken = token;
    }

    /// <summary>Token świeżo wydany w tym zadaniu liczy się jak odświeżony: jego 401 nie wymusza kolejnego.</summary>
    public void NoteIssued()
    {
        lock (_lock) _refreshUsed = true;
    }

    /// <summary>
    /// Czy po 401 z tym tokenem wolno wymusić nowy token. Zużywa jedyne odświeżenie zadania;
    /// <c>false</c> gdy już zużyte albo token już zadziałał (401 wynika wtedy z endpointu, nie z tokenu).
    /// </summary>
    public bool TryBeginRefresh(string rejectedToken)
    {
        lock (_lock)
        {
            if (_refreshUsed || _confirmedToken == rejectedToken) return false;
            _refreshUsed = true;
            return true;
        }
    }
}
