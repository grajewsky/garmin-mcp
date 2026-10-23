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
}
