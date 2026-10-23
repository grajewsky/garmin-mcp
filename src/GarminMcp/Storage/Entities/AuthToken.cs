using System.Text.Json;

namespace GarminMcp.Storage.Entities;

public class AuthToken
{
    public int Id { get; set; } = 1;
    public string Token { get; set; } = null!;
    public DateTimeOffset ExpiresAt { get; set; }
}
