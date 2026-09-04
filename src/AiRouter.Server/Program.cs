using AiRouter.Persistence.Sqlite;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;

var builder = WebApplication.CreateBuilder(args);

var dataPath = builder.Configuration["AIROUTER_DATA_PATH"];
if (string.IsNullOrWhiteSpace(dataPath))
    dataPath = "/data/ai-router.db";

dataPath = Path.GetFullPath(dataPath);
var dataDirectory = Path.GetDirectoryName(dataPath);
if (!string.IsNullOrWhiteSpace(dataDirectory))
    Directory.CreateDirectory(dataDirectory);

builder.Services.AddAiRouterSqlite($"Data Source={dataPath}");
builder.Services.AddAiRouter();
builder.Services.AddOpenAiCompatibleProvider();
builder.Services.AddAiRouterAspNetCore();

var app = builder.Build();

await app.Services.GetRequiredService<IProviderManager>().InitializeAsync();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAiRouterOpenAiEndpoints(builder.Configuration["AIROUTER_API_KEY"]);

var adminKey = builder.Configuration["AIROUTER_ADMIN_KEY"];
if (!string.IsNullOrWhiteSpace(adminKey))
    app.MapAiRouterManagementEndpoints(adminKey);

app.Run();

public partial class Program;
