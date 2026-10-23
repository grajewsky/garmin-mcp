namespace GarminMcp.Garmin;

/// <summary>
/// Okno dat zakotwiczone w anchorze. <see cref="NominalEnd"/> to koniec pełnego okna,
/// <see cref="RequestEnd"/> to koniec przycięty do zakresu żądania.
/// </summary>
public readonly record struct ChunkSpan(DateOnly Start, DateOnly NominalEnd, DateOnly RequestEnd);

/// <summary>
/// Dzieli zakres dat na okna zakotwiczone w stałej dacie (anchor) i decyduje, które okna pobrać ponownie.
/// </summary>
public static class DateChunker
{
    /// <summary>
    /// Zwraca okna chunk i = [anchor + i·days, anchor + (i+1)·days − 1], przecinające [from, to].
    /// Ostatnie okno jest przycinane do <paramref name="to"/>. from &lt; anchor traktowane jak anchor.
    /// </summary>
    public static IReadOnlyList<ChunkSpan> Chunk(DateOnly anchor, DateOnly from, DateOnly to, int days)
    {
        if (days < 1)
            throw new ArgumentOutOfRangeException(nameof(days), days, "Chunk length must be at least 1 day.");

        var start = from < anchor ? anchor : from;
        if (start > to)
            return [];

        var firstIndex = (start.DayNumber - anchor.DayNumber) / days;
        var lastIndex = (to.DayNumber - anchor.DayNumber) / days;

        var spans = new List<ChunkSpan>(lastIndex - firstIndex + 1);
        for (var i = firstIndex; i <= lastIndex; i++)
        {
            var chunkStart = anchor.AddDays(i * days);
            var nominalEnd = chunkStart.AddDays(days - 1);
            var requestEnd = nominalEnd < to ? nominalEnd : to;
            spans.Add(new ChunkSpan(chunkStart, nominalEnd, requestEnd));
        }
        return spans;
    }

    /// <summary>
    /// Okno wymaga pobrania, gdy wymuszono, nie pobrano go nigdy, albo było pobrane przed nominalEnd + tailDays
    /// (czyli nadal może się zmieniać po stronie Garmina).
    /// </summary>
    public static bool NeedsFetch(DateOnly nominalEnd, DateTimeOffset? fetchedAt, bool force, int tailDays)
    {
        if (force || fetchedAt is null)
            return true;

        return DateOnly.FromDateTime(fetchedAt.Value.UtcDateTime) < nominalEnd.AddDays(tailDays);
    }
}
