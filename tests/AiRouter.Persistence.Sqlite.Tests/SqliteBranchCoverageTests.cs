using AiRouter.Persistence.Sqlite;
using AiRouter.Providers;
using Microsoft.Data.Sqlite;

namespace AiRouter.Persistence.Sqlite.Tests;

public sealed class SqliteBranchCoverageTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"ai-router-branches-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Provider_round_trip_covers_null_timeout_models_and_headers()
    {
        var store = Store();
        await store.UpsertAsync(new ProviderDefinition(
            "primary", "Primary", "openai-compatible", "https://example.test/v1", null,
            Timeout: null, Models: null, ExtraHeaders: null));

        var loaded = await store.GetAsync("primary");

        Assert.NotNull(loaded);
        Assert.Null(loaded.Timeout);
        Assert.Empty(loaded.Models!);
        Assert.Empty(loaded.ExtraHeaders!);
    }

    [Fact]
    public async Task Null_json_columns_fall_back_to_empty_collections()
    {
        var store = Store();
        await store.UpsertAsync(new ProviderDefinition(
            "primary", "Primary", "openai-compatible", "https://example.test/v1", null,
            Timeout: TimeSpan.FromSeconds(1), Models: ["model"], ExtraHeaders: new Dictionary<string, string> { ["X-Test"] = "yes" }));

        await using (var connection = new SqliteConnection($"Data Source={_path}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Providers SET ModelsJson = 'null', ExtraHeadersJson = 'null' WHERE Id = 'primary'";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var loaded = await Store().GetAsync("primary");

        Assert.NotNull(loaded);
        Assert.Equal(TimeSpan.FromSeconds(1), loaded.Timeout);
        Assert.Empty(loaded.Models!);
        Assert.Empty(loaded.ExtraHeaders!);
    }

    private SqliteProviderStore Store() => new(new SqliteStoreOptions($"Data Source={_path}"));

    public void Dispose()
    {
        foreach (var suffix in new[] { "", "-shm", "-wal" })
        {
            var file = _path + suffix;
            if (File.Exists(file)) File.Delete(file);
        }
    }
}
