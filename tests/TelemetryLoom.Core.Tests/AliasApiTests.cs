using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class AliasApiTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "telemetry-loom-tests",
        Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public AliasApiTests()
    {
        Directory.CreateDirectory(_root);
        var configPath = Path.Combine(_root, "config.json");
        var missingHwmonRoot = Path.Combine(_root, "missing-hwmon");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelemetryLoom:ConfigPath"] = configPath,
                    ["Hwmon:RootPath"] = missingHwmonRoot
                })));
    }

    [Fact]
    public async Task CreatesResolvesAndDeletesAliasOverHttp()
    {
        using var client = _factory.CreateClient();
        var response = await client.PutAsJsonAsync(
            "/api/aliases/cooling.air.intake",
            new
            {
                displayName = "Radiator Intake Air",
                sensorId = SimulatedSensorSource.TemperatureSensorId
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var aliasJson = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            Assert.Equal("cooling.air.intake", aliasJson.RootElement.GetProperty("key").GetString());
            Assert.Equal("Celsius", aliasJson.RootElement.GetProperty("unit").GetString());
        }

        var sensor = await client.GetAsync("/api/sensors/by-alias/cooling.air.intake");
        Assert.Equal(HttpStatusCode.OK, sensor.StatusCode);
        using (var sensorJson = JsonDocument.Parse(await sensor.Content.ReadAsStringAsync()))
        {
            Assert.Equal("cooling.air.intake", sensorJson.RootElement.GetProperty("alias").GetString());
            Assert.Equal("Radiator Intake Air", sensorJson.RootElement.GetProperty("displayName").GetString());
        }

        Assert.True(File.Exists(Path.Combine(_root, "config.json")));
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync("/api/aliases/cooling.air.intake")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/aliases/cooling.air.intake")).StatusCode);
    }

    [Fact]
    public async Task ReturnsUsefulErrorsForInvalidRequests()
    {
        using var client = _factory.CreateClient();

        var invalidKey = await client.PutAsJsonAsync(
            "/api/aliases/Invalid%20Key",
            new { displayName = "Invalid", sensorId = SimulatedSensorSource.TemperatureSensorId });
        var missingSensor = await client.PutAsJsonAsync(
            "/api/aliases/cooling.air.missing",
            new { displayName = "Missing", sensorId = "sensor:missing" });

        Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingSensor.StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
