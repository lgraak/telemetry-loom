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
    public async Task StreamReturnsImmediateServerSentSensorSnapshot()
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
        await using var body = await response.Content.ReadAsStreamAsync(cancellation.Token);
        using var reader = new StreamReader(body);

        var idLine = await reader.ReadLineAsync(cancellation.Token);
        var eventLine = await reader.ReadLineAsync(cancellation.Token);
        var dataLine = await reader.ReadLineAsync(cancellation.Token);
        var separator = await reader.ReadLineAsync(cancellation.Token);

        Assert.StartsWith("id: ", idLine, StringComparison.Ordinal);
        Assert.Equal("event: sensors", eventLine);
        Assert.StartsWith("data: ", dataLine, StringComparison.Ordinal);
        Assert.Equal(string.Empty, separator);

        using var json = JsonDocument.Parse(dataLine![6..]);
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        var eventId = long.Parse(idLine![4..]);
        Assert.True(eventId > 0);
        Assert.Equal(eventId, json.RootElement.GetProperty("sequence").GetInt64());
        Assert.NotEmpty(json.RootElement.GetProperty("sensors").EnumerateArray());
        Assert.Equal("Celsius", json.RootElement.GetProperty("sensors")[0].GetProperty("unit").GetString());
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

    private WebApplicationFactory<Program> CreateFactory(int intervalMilliseconds) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelemetryLoom:ConfigPath"] = Path.Combine(_root, $"config-{intervalMilliseconds}.json"),
                    ["Hwmon:RootPath"] = Path.Combine(_root, "missing-hwmon"),
                    ["TelemetryLoom:LiveUpdates:IntervalMilliseconds"] = intervalMilliseconds.ToString()
                })));
}
