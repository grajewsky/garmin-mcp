using GarminMcp.Garmin;

namespace GarminMcp.Tests.Support;

public sealed class InMemoryTokenStore : ITokenStore
{
    public StoredToken? Current { get; set; }

    public Task<StoredToken?> LoadAsync(CancellationToken ct) => Task.FromResult(Current);

    public Task SaveAsync(StoredToken token, CancellationToken ct)
    {
        Current = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct)
    {
        Current = null;
        return Task.CompletedTask;
    }
}
