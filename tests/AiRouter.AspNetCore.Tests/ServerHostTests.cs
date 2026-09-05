using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace AiRouter.AspNetCore.Tests;

public sealed class ServerHostTests
{
    [Fact]
    public async Task Health_is_available_and_management_is_not_mapped_without_admin_key()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/providers")).StatusCode);
    }

    [Fact]
    public async Task Fresh_server_seeds_opencode_free_provider_without_api_key()
    {
        await using var factory = Factory();
        var client = factory.CreateClient();

        using var response = await client.GetAsync("/v1/models");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ids = document.RootElement.GetProperty("data")
            .EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToArray();

        Assert.Contains("opencode-free/mimo-v2.5-free", ids);
    }

    [Fact]
    public async Task Admin_key_enables_and_protects_management_routes()
    {
        await using var factory = Factory(("AIROUTER_ADMIN_KEY", "admin-secret"));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/providers")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "admin-secret");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/providers")).StatusCode);
    }

    [Fact]
    public async Task Api_key_protects_openai_routes()
    {
        await using var factory = Factory(("AIROUTER_API_KEY", "api-secret"));
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/models")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "api-secret");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/v1/models")).StatusCode);
    }

    [Fact]
    public async Task Configured_data_path_creates_sqlite_database()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ai-router-server-{Guid.NewGuid():N}.db");
        try
        {
            await using var factory = Factory(("AIROUTER_DATA_PATH", path));
            using var client = factory.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
            Assert.True(File.Exists(path), $"Expected SQLite database at {path}");
        }
        finally
        {
            DeleteDatabase(path);
        }
    }

    private static WebApplicationFactory<Program> Factory(params (string Key, string? Value)[] values)
    {
        var settings = values.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        if (!settings.ContainsKey("AIROUTER_DATA_PATH"))
            settings["AIROUTER_DATA_PATH"] = Path.Combine(Path.GetTempPath(), $"ai-router-test-{Guid.NewGuid():N}.db");

        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(settings);
            });
        });
    }

    private static void DeleteDatabase(string path)
    {
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
        if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
    }
}
