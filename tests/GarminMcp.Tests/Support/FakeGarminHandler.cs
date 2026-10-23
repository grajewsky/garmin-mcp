namespace GarminMcp.Tests.Support;

/// <summary>Atrapa Garmina: reguły po prefiksie ścieżki (ostatnia pasująca wygrywa), rejestruje wywołania.</summary>
public sealed class FakeGarminHandler : HttpMessageHandler
{
    private readonly List<(string Prefix, Func<HttpRequestMessage, HttpResponseMessage> Respond)> _rules = [];
    private readonly object _lock = new();

    public List<HttpRequestMessage> Calls { get; } = [];

    public FakeGarminHandler On(string pathPrefix, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        lock (_lock) _rules.Add((pathPrefix, respond));
        return this;
    }

    public int CountCalls(string pathPrefix)
    {
        lock (_lock)
            return Calls.Count(c => c.RequestUri!.AbsolutePath.StartsWith(pathPrefix, StringComparison.Ordinal));
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Func<HttpRequestMessage, HttpResponseMessage>? respond = null;
        lock (_lock)
        {
            Calls.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            for (var i = _rules.Count - 1; i >= 0; i--)
            {
                if (path.StartsWith(_rules[i].Prefix, StringComparison.Ordinal))
                {
                    respond = _rules[i].Respond;
                    break;
                }
            }
        }
        return Task.FromResult(respond is null
            ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound) { RequestMessage = request }
            : respond(request));
    }
}
