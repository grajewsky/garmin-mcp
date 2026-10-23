using GarminMcp.Garmin;
using GarminMcp.Storage.Entities;
using Microsoft.EntityFrameworkCore;

namespace GarminMcp.Storage;

public sealed class DbTokenStore(GarminDbContext db) : ITokenStore
{
    public async Task<StoredToken?> LoadAsync(CancellationToken ct)
    {
        var row = await db.AuthTokens.AsNoTracking().SingleOrDefaultAsync(t => t.Id == 1, ct);
        return row is null ? null : new StoredToken(row.Token, row.ExpiresAt);
    }

    public async Task SaveAsync(StoredToken token, CancellationToken ct)
    {
        var row = await db.AuthTokens.SingleOrDefaultAsync(t => t.Id == 1, ct);
        if (row is null)
            db.AuthTokens.Add(new AuthToken { Id = 1, Token = token.Token, ExpiresAt = token.ExpiresAt });
        else
        {
            row.Token = token.Token;
            row.ExpiresAt = token.ExpiresAt;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearAsync(CancellationToken ct) =>
        await db.AuthTokens.Where(t => t.Id == 1).ExecuteDeleteAsync(ct);
}
