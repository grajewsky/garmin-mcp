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

    /// <summary>Tworzy świeżą bazę t_&lt;guid&gt;, stosuje migracje i zwraca kontekst na niej.</summary>
    public async Task<GarminDbContext> CreateContextAsync()
    {
        var dbName = "t_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE {dbName}", admin);
            await cmd.ExecuteNonQueryAsync();
        }

        var cs = new NpgsqlConnectionStringBuilder(ConnectionString) { Database = dbName, Pooling = false }.ConnectionString;
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

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
