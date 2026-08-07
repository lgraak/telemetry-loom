using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class BrowserPagesTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
    private readonly string _configPath;
    private readonly WebApplicationFactory<Program> _factory;

    public BrowserPagesTests()
    {
        Directory.CreateDirectory(_root);
        _configPath = Path.Combine(_root, "config.json");
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelemetryLoom:ConfigPath"] = _configPath,
                    ["Hwmon:RootPath"] = Path.Combine(_root, "missing-hwmon")
                }));
            builder.ConfigureServices(services =>
                services.AddSingleton<ISensorSource>(new BrowserFixtureSensorSource()));
        });
    }

    [Fact]
    public async Task OverviewRendersReadOnlyConfigurationStatus()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Telemetry Loom is running", html, StringComparison.Ordinal);
        Assert.Contains(_configPath, html, StringComparison.Ordinal);
        Assert.Contains($"{_configPath}.previous", html, StringComparison.Ordinal);
        Assert.Contains("None since startup", html, StringComparison.Ordinal);
        Assert.Contains("Out-of-process edits", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"submit\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SensorsRenderGroupedPresentationAndDistinctStableIdentitySafely()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/sensors");
        var html = await response.Content.ReadAsStringAsync();
        var script = await client.GetStringAsync("/js/sensors.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("AMD GPU", html, StringComparison.Ordinal);
        Assert.Contains("GPU Edge Temperature", html, StringComparison.Ordinal);
        Assert.Contains("Raw: edge", html, StringComparison.Ordinal);
        Assert.Contains("Temperature reported for the edge sensor", html, StringComparison.Ordinal);
        Assert.Contains("browser-fixture:gpu:edge", html, StringComparison.Ordinal);
        Assert.Contains("Available", html, StringComparison.Ordinal);
        Assert.Contains("Stale", html, StringComparison.Ordinal);
        Assert.Contains("Unavailable", html, StringComparison.Ordinal);
        Assert.Contains("—", html, StringComparison.Ordinal);
        Assert.Contains("°C", html, StringComparison.Ordinal);
        Assert.Contains("2026-08-07 12:00:00Z", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;unsafe()&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>unsafe()</script>", html, StringComparison.Ordinal);
        Assert.Contains("/api/sensors/stream", script, StringComparison.Ordinal);
        Assert.DoesNotContain("fetch(\"/api/sensors\"", script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/aliases", "not implemented in Slice 7A")]
    [InlineData("/calculations", "not implemented in Slice 7A")]
    public async Task DeferredNavigationDestinationsRenderWithoutWriteControls(string path, string message)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(message, html, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", html, StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class BrowserFixtureSensorSource : ISensorSource
    {
        private static readonly DateTimeOffset Timestamp = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        private static readonly IReadOnlyList<SensorReading> Readings =
        [
            Reading("browser-fixture:gpu:edge", "edge", 42, SensorStatus.Available, "amdgpu", "gpu-1"),
            Reading("browser-fixture:gpu:junction", "junction", 55, SensorStatus.Stale, "amdgpu", "gpu-1"),
            Reading("browser-fixture:unsafe", "<script>unsafe()</script>", null, SensorStatus.Unavailable, "unsafe", "unsafe-1")
        ];

        public string Name => "hwmon";
        public IReadOnlyList<SensorReading> GetSensors() => Readings;
        public SensorReading? GetSensor(string id) => Readings.FirstOrDefault(reading => reading.Id == id);

        private static SensorReading Reading(
            string id,
            string rawLabel,
            double? value,
            SensorStatus status,
            string driver,
            string deviceKey) =>
            new(
                id,
                rawLabel,
                null,
                value,
                QuantityKind.Temperature,
                UnitCode.Celsius,
                "°C",
                "hwmon",
                status,
                value is null ? null : Timestamp,
                new Dictionary<string, string>
                {
                    ["driver"] = driver,
                    ["deviceKey"] = deviceKey,
                    ["measurement"] = "input"
                });
    }
}
