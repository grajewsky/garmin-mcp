using System.Text.Json;

namespace GarminMcp.Tests.Mapping;

internal static class Fixture
{
    public static JsonDocument Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));
}
