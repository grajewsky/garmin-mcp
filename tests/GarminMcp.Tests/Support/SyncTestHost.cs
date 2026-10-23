using GarminMcp.Garmin;
using GarminMcp.Storage;
using GarminMcp.Sync;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarminMcp.Tests.Support;

/// <summary>Prawdziwy kontener DI na bazie testowej i atrapie Garmina; odpowiednik produkcyjnego wiązania z Program.cs.</summary>
public sealed class SyncTestHost
{
    public required GarminDbContext Db { get; init; }
    public required FakeGarminHandler Handler { get; init; }
    public required SyncJobRunner Runner { get; init; }
    public required InMemoryTokenStore Store { get; init; }

    public static async Task<SyncTestHost> CreateAsync(PostgresFixture fx, IReadOnlyList<HealthDataset>? datasets = null)
    {
        var db = await fx.CreateContextAsync();
        var handler = new FakeGarminHandler();
        var store = new InMemoryTokenStore();
        var garmin = TestGarmin.CreateParts(handler, store);
        var cs = db.Database.GetConnectionString()!;

        var services = new ServiceCollection();
        services.AddDbContext<GarminDbContext>(o => o.UseNpgsql(cs).UseSnakeCaseNamingConvention());
        // Jak w Program.cs: sesja uwierzytelnienia per zadanie (scope), klient przejściowy.
        services.AddScoped<GarminAuthSession>();
        services.AddTransient(sp => garmin.NewClient(sp.GetRequiredService<GarminAuthSession>()));
        services.AddSingleton(garmin.Tokens);
        services.AddSingleton(Options.Create(new GarminOptions { RequestDelay = TimeSpan.Zero }));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ActivityRecordWriter>();
        services.AddScoped<ActivitySync>();
        services.AddScoped<HealthSync>();
        var provider = services.BuildServiceProvider();

        var runner = new SyncJobRunner(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System,
            NullLogger<SyncJobRunner>.Instance) { HealthDatasetsOverride = datasets ?? [] };
        return new SyncTestHost { Db = db, Handler = handler, Runner = runner, Store = store };
    }
}
