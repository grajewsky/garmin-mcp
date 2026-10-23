using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarminMcp.Tests.Support;

[Collection("postgres")]
public class PostgresFixtureTests(PostgresFixture fixture)
{
    [Fact]
    public async Task CanConnect()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT 1", connection);

        var result = await command.ExecuteScalarAsync();

        Assert.Equal(1, Convert.ToInt32(result));
    }

    [Fact]
    public async Task Server_AllowsManyConnections_ForPerTestPools()
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SHOW max_connections", connection);

        Assert.Equal("1000", (string?)await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task PerTestDatabases_AreSeparate_AndPooled()
    {
        await using var a = await fixture.CreateContextAsync();
        await using var b = await fixture.CreateContextAsync();
        var csA = new NpgsqlConnectionStringBuilder(a.Database.GetConnectionString());

        Assert.NotEqual(csA.Database, new NpgsqlConnectionStringBuilder(b.Database.GetConnectionString()).Database);
        Assert.True(csA.Pooling);
    }
}
