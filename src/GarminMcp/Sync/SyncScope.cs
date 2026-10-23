namespace GarminMcp.Sync;

/// <summary>Zakres synchronizacji; <c>Force</c> (podany zakres) wymusza ponowne pobranie szczegółów aktywności w zakresie.</summary>
public sealed record SyncScope(DateOnly? From, DateOnly? To)
{
    public bool Force => From is not null || To is not null;
}

/// <summary>Błąd przerywający synchronizację, który nie jest błędem autoryzacji.</summary>
public sealed class SyncException(string message) : Exception(message);
