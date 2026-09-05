using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using AiRouter.Providers;
using AiRouter.Routing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace AiRouter.AspNetCore.Tests;

public sealed class AspNetResidualCoverageTests
{
    [Fact]
    public async Task Model_discovery_rethrows_request_cancellation()
    {
        var manager = new CancellingDiscoveryManager();
        await using var app = await StartAsync(
            new StaticRouter(() => Success()),
            manager);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            app.GetTestClient().GetAsync("/v1/models", cts.Token));

        Assert.True(manager.ListModelsStarted);
    }

    [Fact]
    public async Task Streaming_endpoint_awaits_asynchronous_stream_disposal()
    {
        var stream = new AsyncDisposeMemoryStream(Encoding.UTF8.GetBytes("data: ok\n\n"));
        await using var app = await StartAsync(new StaticRouter(() => new RouterResult
        {
            Success = true,
            StatusCode = 200,
            Stream = stream
        }));

        var response = await app.GetTestClient().PostAsJsonAsync(
            "/v1/chat/completions",
            new { model = "m", stream = true, messages = Array.Empty<object>() });

        Assert.Equal("data: ok\n\n", await response.Content.ReadAsStringAsync());
        Assert.True(stream.DisposeAsyncCalled);
    }

    [Fact]
    public async Task Management_json_reader_handles_bad_http_request_exception()
    {
        var handlers = typeof(AiRouterManagementEndpointRouteBuilderExtensions).Assembly
            .GetType("Microsoft.AspNetCore.Builder.ManagementHandlers", throwOnError: true)!;
        var readAsync = handlers.GetMethod("ReadAsync", BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(typeof(ProviderDefinition));
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";
        context.Request.Body = new BadRequestStream();
        context.Response.Body = new MemoryStream();

        var task = (Task<ProviderDefinition?>)readAsync.Invoke(null, [context])!;
        var result = await task;

        Assert.Null(result);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public void Bearer_authorizer_rejects_raw_empty_bearer_token()
    {
        var authorizer = typeof(AiRouterManagementEndpointRouteBuilderExtensions).Assembly
            .GetType("AiRouter.AspNetCore.BearerKeyAuthorizer", throwOnError: true)!;
        var method = authorizer.GetMethod("IsAuthorized", BindingFlags.Public | BindingFlags.Static)!;
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer ";

        var authorized = (bool)method.Invoke(null, [context, "secret"])!;

        Assert.False(authorized);
    }

    private static async Task<WebApplication> StartAsync(IAiRouter router, IProviderManager? manager = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(router);
        builder.Services.AddSingleton(manager ?? new EmptyManager());
        builder.Services.AddSingleton<IRouteStore>(new InMemoryRouteStore());
        builder.Services.AddAiRouterAspNetCore();
        var app = builder.Build();
        app.MapAiRouterOpenAiEndpoints();
        await app.StartAsync();
        return app;
    }

    private static RouterResult Success() => new()
    {
        Success = true,
        StatusCode = 200,
        Body = JsonSerializer.SerializeToElement(new { ok = true })
    };

    private sealed class StaticRouter(Func<RouterResult> result) : IAiRouter
    {
        public Task<RouterResult> ChatAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) =>
            Task.FromResult(result());
        public Task<RouterResult> ResponsesAsync(string model, JsonElement body, bool stream = false, CancellationToken ct = default) =>
            Task.FromResult(result());
    }

    private class EmptyManager : IProviderManager
    {
        public virtual IReadOnlyList<IAiProvider> Snapshot => [];
        public virtual Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public virtual Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProviderDefinition>>([]);
        public virtual Task<ProviderDefinition?> GetAsync(string id, CancellationToken ct = default) => Task.FromResult<ProviderDefinition?>(null);
        public virtual Task<ProviderDefinition> AddAsync(ProviderDefinition provider, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ProviderDefinition> UpdateAsync(string id, ProviderDefinition provider, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ProviderDefinition> SetEnabledAsync(string id, bool enabled, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<ProviderConnectivityResult> TestAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public virtual Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class CancellingDiscoveryManager : EmptyManager
    {
        private readonly ProviderDefinition _provider = new(
            "discover",
            "Discover",
            "fake",
            "https://unused.test",
            null,
            Models: [],
            DiscoverModels: true);

        public bool ListModelsStarted { get; private set; }

        public override Task<IReadOnlyList<ProviderDefinition>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProviderDefinition>>([_provider]);

        public override async Task<IReadOnlyList<string>> ListModelsAsync(string id, CancellationToken ct = default)
        {
            ListModelsStarted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return [];
        }
    }

    private sealed class AsyncDisposeMemoryStream(byte[] bytes) : MemoryStream(bytes)
    {
        public bool DisposeAsyncCalled { get; private set; }
        public override async ValueTask DisposeAsync()
        {
            await Task.Yield();
            DisposeAsyncCalled = true;
            Dispose();
        }
    }

    private sealed class BadRequestStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new BadHttpRequestException("bad request");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new BadHttpRequestException("bad request"));
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
