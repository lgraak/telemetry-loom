using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace TelemetryLoom.Core.Tests;

public sealed class LiveUpdateApiTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public LiveUpdateApiTests()
    {
        Directory.CreateDirectory(_root);
        _factory = CreateFactory(100);
    }

    [Fact]
    public async Task StreamReturnsImmediateAndSubsequentServerSentSensorSnapshots()
    {
        using var client = _factory.CreateClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/sensors/stream");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellation.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.NoCache == true);
        Assert.DoesNotContain(
            "keep-alive",
            response.Headers.Connection,
            StringComparer.OrdinalIgnoreCase);
        await using var body = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(body);

        var first = await ReadEvent(reader, cancellation.Token);
        var second = await ReadEvent(reader, cancellation.Token);

        Assert.True(first.Id > 0);
        Assert.True(second.Id > first.Id);
        AssertSnapshot(first);
        AssertSnapshot(second);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(60001)]
    public void InvalidConfiguredIntervalPreventsStartup(int intervalMilliseconds)
    {
        using var factory = CreateFactory(intervalMilliseconds);
        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("IntervalMilliseconds must be between 100 and 60000", exception.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private static async Task<SseEvent> ReadEvent(StreamReader reader, CancellationToken cancellationToken)
    {
        var idLine = await reader.ReadLineAsync(cancellationToken);
        var eventLine = await reader.ReadLineAsync(cancellationToken);
        var dataLine = await reader.ReadLineAsync(cancellationToken);
        var separator = await reader.ReadLineAsync(cancellationToken);

        Assert.StartsWith("id: ", idLine, StringComparison.Ordinal);
        Assert.Equal("event: sensors", eventLine);
        Assert.StartsWith("data: ", dataLine, StringComparison.Ordinal);
        Assert.Equal(string.Empty, separator);
        return new SseEvent(long.Parse(idLine![4..]), dataLine![6..]);
    }

    private static void AssertSnapshot(SseEvent snapshot)
    {
        using var json = JsonDocument.Parse(snapshot.Json);
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(snapshot.Id, json.RootElement.GetProperty("sequence").GetInt64());
        Assert.NotEmpty(json.RootElement.GetProperty("sensors").EnumerateArray());
        Assert.Equal("Celsius", json.RootElement.GetProperty("sensors")[0].GetProperty("unit").GetString());
        Assert.Equal(
            "Simulated Temperature",
            json.RootElement.GetProperty("sensors")[0].GetProperty("presentation").GetProperty("rawLabel").GetString());
    }

    private WebApplicationFactory<Program> CreateFactory(int intervalMilliseconds) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelemetryLoom:ConfigPath"] = Path.Combine(_root, $"config-{intervalMilliseconds}.json"),
                    ["Hwmon:RootPath"] = Path.Combine(_root, "missing-hwmon"),
                    ["TelemetryLoom:LiveUpdates:IntervalMilliseconds"] = intervalMilliseconds.ToString()
                })));

    private sealed record SseEvent(long Id, string Json);
}
