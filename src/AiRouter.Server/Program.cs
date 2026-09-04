using AiRouter.Persistence.Sqlite;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;
using AiRouter.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAiRouter();
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var dataPath = configuration["AIROUTER_DATA_PATH"];
    if (string.IsNullOrWhiteSpace(dataPath))
        dataPath = "/data/ai-router.db";

    dataPath = Path.GetFullPath(dataPath);
    var dataDirectory = Path.GetDirectoryName(dataPath);
    if (!string.IsNullOrWhiteSpace(dataDirectory))
        Directory.CreateDirectory(dataDirectory);

    return new SqliteStoreOptions($"Data Source={dataPath}");
});
builder.Services.AddSingleton<IProviderStore, SqliteProviderStore>();
builder.Services.AddSingleton<IRouteStore, SqliteRouteStore>();
builder.Services.AddOpenAiCompatibleProvider();
builder.Services.AddAiRouterAspNetCore();

var app = builder.Build();

await app.Services.GetRequiredService<IProviderManager>().InitializeAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAiRouterOpenAiEndpoints(app.Configuration["AIROUTER_API_KEY"]);

var adminKey = app.Configuration["AIROUTER_ADMIN_KEY"];
if (!string.IsNullOrWhiteSpace(adminKey))
    app.MapAiRouterManagementEndpoints(adminKey);

app.Run();

public partial class Program;
