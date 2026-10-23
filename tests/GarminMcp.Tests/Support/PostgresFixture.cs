using GarminMcp.Storage;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace GarminMcp.Tests.Support;

/// <summary>
/// Jeden kontener Postgresa na cały przebieg testów, współdzielony przez kolekcję "postgres".
/// <see cref="ConnectionString"/> wskazuje bazę administracyjną serwera (do tworzenia baz per test).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>
    /// Tworzy świeżą bazę t_&lt;guid&gt;, stosuje migracje i zwraca kontekst na niej. Izolacja przez unikalną nazwę
    /// bazy (bazy nie są usuwane — znikają z kontenerem), więc pula połączeń nie koliduje z DROP DATABASE.
    /// </summary>
    public async Task<GarminDbContext> CreateContextAsync()
    {
        var dbName = "t_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {dbName}", admin);
            await cmd.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = dbName }.ConnectionString;
        var db = NewContext(cs);
        await db.Database.MigrateAsync();
        return db;
    }

    /// <summary>Drugi kontekst na tej samej bazie co <paramref name="existing"/> (np. odczyt w nowym kontekście).</summary>
    public static GarminDbContext OpenAnother(GarminDbContext existing) =>
        NewContext(existing.Database.GetConnectionString()!);

    private static GarminDbContext NewContext(string connectionString) =>
        new(new DbContextOptionsBuilder<GarminDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await WaitUntilReachableAsync(TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Kontener bywa "gotowy" zanim port opublikowany na hoście przyjmuje połączenia — ponawiaj połączenie
    /// i <c>SELECT 1</c> z hosta, aż się uda albo minie limit.
    /// </summary>
    private async Task WaitUntilReachableAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                await using var conn = new NpgsqlConnection(ConnectionString);
                await conn.OpenAsync();
                await using var cmd = new NpgsqlCommand("SELECT 1", conn);
                await cmd.ExecuteScalarAsync();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                NpgsqlConnection.ClearAllPools();
                await Task.Delay(250);
            }
        }
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
