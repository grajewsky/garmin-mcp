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

    public Task InitializeAsync() => _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
