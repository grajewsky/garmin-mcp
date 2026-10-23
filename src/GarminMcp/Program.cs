using GarminMcp.Garmin;
using GarminMcp.Hosting;
using GarminMcp.Storage;
using GarminMcp.Sync;
using GarminMcp.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<GarminOptions>(builder.Configuration.GetSection("Garmin"));
builder.Services.AddDbContext<GarminDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Garmin"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddSingleton(TimeProvider.System);

// Logowanie HttpClient wyciszone w appsettings.json (System.Net.Http.HttpClient = Warning).
builder.Services.AddHttpClient<TokenProvider>(ConfigureGarminClient);
builder.Services.AddHttpClient<GarminApiClient>(ConfigureGarminClient);

builder.Services.AddScoped<ITokenStore, DbTokenStore>();
// Jedno wymuszone odświeżenie tokenu na zadanie synchronizacji: sesja współdzielona przez klientów w scope zadania.
builder.Services.AddScoped<GarminAuthSession>();
builder.Services.AddScoped<ActivityRecordWriter>();
builder.Services.AddScoped<ActivitySync>();
builder.Services.AddScoped<HealthSync>();
builder.Services.AddSingleton<SyncJobRunner>();

builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<SyncTools>()
    .WithTools<ActivityTools>()
    .WithTools<HealthTools>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<GarminDbContext>().Database.MigrateAsync();
}
await app.Services.GetRequiredService<SyncJobRunner>().MarkInterruptedAsync();

app.UseLoopbackOriginCheck("/mcp");
app.MapMcp("/mcp");
app.MapGet("/healthz", () => "ok");

app.Run();

static void ConfigureGarminClient(IServiceProvider sp, HttpClient http)
{
    http.BaseAddress = new Uri(sp.GetRequiredService<IOptions<GarminOptions>>().Value.BaseUrl);
    http.Timeout = TimeSpan.FromSeconds(60);
}

public partial class Program;
