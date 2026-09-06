using AiRouter.Persistence.Sqlite;
using AiRouter.Providers;
using AiRouter.Providers.OpenAI;
using AiRouter.Routing;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAiRouter();
builder.Services.AddSingleton(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();
    var dataPath = ServerDataPath.Resolve(configuration["AIROUTER_DATA_PATH"]);
    Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
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

app.Use(async (context, next) =>
{
    if (string.Equals(context.Request.Path.Value, "/admin", StringComparison.Ordinal))
    {
        context.Response.Redirect("/admin/");
        return;
    }

    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.UseAiRouter(bearerKey: app.Configuration["AIROUTER_API_KEY"]);

var adminKey = app.Configuration["AIROUTER_ADMIN_KEY"];
if (!string.IsNullOrWhiteSpace(adminKey))
{
    app.MapAiRouterManagementEndpoints(adminKey);
    app.MapAiRouterConfigurationManagementEndpoints(adminKey);
    app.MapAiRouterTelemetryManagementEndpoints(adminKey);
}

app.MapFallbackToFile("/admin/{*path:nonfile}", "admin/index.html");

app.Run();

public partial class Program;
