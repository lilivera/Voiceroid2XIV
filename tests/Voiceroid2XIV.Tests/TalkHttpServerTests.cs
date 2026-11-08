using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Voiceroid2XIV.Core;
using Voiceroid2XIV.Runtime;

namespace Voiceroid2XIV.Tests;

public sealed class TalkHttpServerTests : IAsyncLifetime
{
    private readonly SpeechQueue queue = new();
    private readonly HttpClient client = new() { Timeout = TimeSpan.FromSeconds(15) };
    private TalkHttpServer server = null!;
    private int port;

    public async Task InitializeAsync()
    {
        port = FindPort();
        client.BaseAddress = new Uri($"http://127.0.0.1:{port}");
        server = new TalkHttpServer(port, text => Task.FromResult(queue.Submit(text)));
        await server.StartAsync();
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    public async Task GetAcceptsJapaneseTextAndLocalhost(string host)
    {
        const string text = "結月ゆかり＞日本語のテスト & 記号+";
        using var response = await client.GetAsync($"http://{host}:{port}/talk?text={Uri.EscapeDataString(text)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(text, json.RootElement.GetProperty("text").GetString());
        Assert.True(queue.TryTake(false, 0, out var spoken));
        Assert.Equal(text, spoken);
    }

    [Fact]
    public async Task JsonPostPreservesNewlinesAndProcessesCommands()
    {
        using var body = new StringContent(JsonSerializer.Serialize(new { text = "<pause>日本語\n二行目" }), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/talk", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(queue.Status().Paused);
        Assert.Equal(1, queue.Status().QueueCount);
    }

    [Fact]
    public async Task FormPostDecodesText()
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["text"] = "日本語 + テスト" });
        using var response = await client.PostAsync("/talk", body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        queue.TryTake(false, 0, out var text);
        Assert.Equal("日本語 + テスト", text);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"text\":42}")]
    [InlineData("{\"text\":null}")]
    [InlineData("{\"text\":\" \"}")]
    public async Task InvalidJsonOrTextReturns400(string payload)
    {
        using var body = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/talk", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, queue.Status().QueueCount);
    }

    [Fact]
    public async Task MissingQueryReturns400()
    {
        using var response = await client.GetAsync("/talk");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(4097)]
    [InlineData(40000)]
    public async Task OversizedRequestsReturn413(int length)
    {
        using var body = new StringContent(JsonSerializer.Serialize(new { text = new string('a', length) }), Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/talk", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, queue.Status().QueueCount);
    }

    [Fact]
    public async Task UnsupportedContentTypeReturns415()
    {
        using var body = new StringContent("text=test");
        using var response = await client.PostAsync("/talk", body);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("Origin", "https://example.com")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    public async Task CrossOriginBrowserRequestsAreRejected(string header, string value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/talk?text=test");
        request.Headers.Add(header, value);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, queue.Status().QueueCount);
    }

    [Fact]
    public async Task FullQueueReturns503()
    {
        for (var index = 0; index < SpeechQueue.MaximumQueueLength; index++) queue.Submit("待機");
        using var response = await client.GetAsync("/talk?text=test");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/unknown", HttpStatusCode.NotFound)]
    [InlineData("PUT", "/talk", HttpStatusCode.MethodNotAllowed)]
    [InlineData("OPTIONS", "/talk", HttpStatusCode.NoContent)]
    public async Task RoutesAndMethodsAreValidated(string method, string path, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public void OnlyLoopbackInterfacesAreBound()
    {
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()
            .Where(endpoint => endpoint.Port == port).ToArray();
        Assert.NotEmpty(listeners);
        Assert.All(listeners, endpoint => Assert.True(IPAddress.IsLoopback(endpoint.Address), endpoint.ToString()));
    }

    [Fact]
    public async Task PortConflictIsReportedAndDoesNotStopExistingServer()
    {
        await using var duplicate = new TalkHttpServer(port, _ => Task.FromResult(new BridgeResponse { Success = true }));
        await Assert.ThrowsAnyAsync<Exception>(() => duplicate.StartAsync());
        await duplicate.DisposeAsync();
        using var response = await client.GetAsync("/talk?text=existing");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DisposeReleasesPortForNextInstance()
    {
        await server.DisposeAsync();
        server = new TalkHttpServer(port, text => Task.FromResult(queue.Submit(text)));
        await server.StartAsync();
        Assert.True(server.IsRunning);
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await server.DisposeAsync();
    }

    private static int FindPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
