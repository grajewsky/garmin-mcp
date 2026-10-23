using System.Net;
using System.Text;
using GarminMcp.Garmin;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GarminMcp.Tests.Support;

public static class TestGarmin
{
    public const string TokenPrefix = "/personalapitoken-service";

    public static (GarminApiClient Client, TokenProvider Tokens) Create(
        FakeGarminHandler handler, ITokenStore? store = null, TimeProvider? time = null, string? keyFile = null)
    {
        keyFile ??= WriteKeyFile();
        var options = Options.Create(new GarminOptions
        {
            RequestDelay = TimeSpan.Zero,
            RetryBaseDelay = TimeSpan.Zero,
            ApiKeyFile = keyFile,
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri(options.Value.BaseUrl) };
        time ??= TimeProvider.System;
        // Domyślna reguła tokenu; testy mogą ją nadpisać kolejnym On(...).
        handler.On(TokenPrefix, _ => Text(Jwt(time.GetUtcNow().AddHours(24))));
        var tokens = new TokenProvider(http, store ?? new InMemoryTokenStore(), options, time);
        var client = new GarminApiClient(http, tokens, options, NullLogger<GarminApiClient>.Instance);
        return (client, tokens);
    }

    public static string WriteKeyFile(string content = "test-key\n")
    {
        var path = Path.Combine(Path.GetTempPath(), $"garmin-key-{Guid.NewGuid():N}");
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>JWT bez prawdziwego podpisu, z claimem <c>exp</c>.</summary>
    public static string Jwt(DateTimeOffset exp)
    {
        static string B64(string s) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{exp.ToUnixTimeSeconds()}}}")}.sig";
    }

    public static HttpResponseMessage Text(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "text/plain") };

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}
