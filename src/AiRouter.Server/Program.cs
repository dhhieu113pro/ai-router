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
    dataPath = string.IsNullOrWhiteSpace(dataPath) ? "/data/ai-router.db" : dataPath;

    dataPath = Path.GetFullPath(dataPath);
    var dataDirectory = Path.GetDirectoryName(dataPath);
    if (!string.IsNullOrWhiteSpace(dataDirectory))
        Directory.CreateDirectory(dataDirectory);

    return new SqliteStoreOptions($"Data Source={dataPath}");
});
builder.Services.AddSingleton<IProviderStore, SqliteProviderStore>();
builder.Services.AddSingleton<IRouteStore, SqliteRouteStore>();
builder.Services.AddOpenAiCompatibleProvider();

var app = builder.Build();

var providerManager = app.Services.GetRequiredService<IProviderManager>();
await providerManager.InitializeAsync();
if ((await providerManager.ListAsync()).Count == 0)
{
    await providerManager.AddAsync(new ProviderDefinition(
        Id: "opencode-free",
        Name: "OpenCode Free",
        Type: "openai-compatible",
        BaseUrl: "https://opencode.ai/inference/openai/v1/",
        ApiKey: null,
        Models: ["mimo-v2.5-free"],
        DefaultModel: "mimo-v2.5-free",
        DiscoverModels: false,
        SupportsNativeResponses: false));
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.UseAiRouter(bearerKey: app.Configuration["AIROUTER_API_KEY"]);

var adminKey = app.Configuration["AIROUTER_ADMIN_KEY"];
if (!string.IsNullOrWhiteSpace(adminKey))
    app.MapAiRouterManagementEndpoints(adminKey);

app.Run();

public partial class Program;