using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Configuration;
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
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelemetryLoom:ConfigPath"] = configPath,
                    ["Hwmon:RootPath"] = missingHwmonRoot
                }));
            builder.ConfigureServices(services =>
                services.AddSingleton<ISensorSource>(new ApiFixtureSensorSource()));
        });
    }

    [Fact]
    public async Task CreatesResolvesAndDeletesAliasOverHttp()
    {
        using var client = _factory.CreateClient();
        using var sensorsJson = JsonDocument.Parse(await client.GetStringAsync("/api/sensors"));
        var fixtureSensorId = sensorsJson.RootElement.EnumerateArray()
            .Single(sensor => sensor.GetProperty("source").GetString() == "api-fixture")
            .GetProperty("id")
            .GetString()!;
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
            Assert.Equal("°C", aliasJson.RootElement.GetProperty("unitSymbol").GetString());
        }

        var sensor = await client.GetAsync("/api/sensors/by-alias/cooling.air.intake");
        Assert.Equal(HttpStatusCode.OK, sensor.StatusCode);
        using (var sensorJson = JsonDocument.Parse(await sensor.Content.ReadAsStringAsync()))
        {
            Assert.Equal("cooling.air.intake", sensorJson.RootElement.GetProperty("alias").GetString());
            Assert.Equal("Radiator Intake Air", sensorJson.RootElement.GetProperty("displayName").GetString());
            var presentation = sensorJson.RootElement.GetProperty("presentation");
            Assert.Equal("Simulated Temperature", presentation.GetProperty("rawLabel").GetString());
            Assert.Equal("Radiator Intake Air", presentation.GetProperty("displayName").GetString());
            Assert.Equal("UserDefined", presentation.GetProperty("interpretationConfidence").GetString());
        }

        var rename = await client.PutAsJsonAsync(
            "/api/aliases/cooling.air.intake",
            new
            {
                displayName = "Case Intake Air",
                sensorId = SimulatedSensorSource.TemperatureSensorId
            });
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);

        var rebind = await client.PutAsJsonAsync(
            "/api/aliases/cooling.air.intake",
            new { displayName = "Hardware Intake Air", sensorId = fixtureSensorId });
        Assert.Equal(HttpStatusCode.OK, rebind.StatusCode);

        var conflict = await client.PutAsJsonAsync(
            "/api/aliases/cooling.air.duplicate",
            new { displayName = "Duplicate", sensorId = fixtureSensorId });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var configPath = Path.Combine(_root, "config.json");
        Assert.True(File.Exists(configPath));
        Assert.DoesNotContain("unitSymbol", await File.ReadAllTextAsync(configPath), StringComparison.Ordinal);
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

    [Fact]
    public async Task RestWriteAdvancesProcessLocalConfigurationRevision()
    {
        using var client = _factory.CreateClient();
        var configuration = _factory.Services.GetRequiredService<TelemetryConfigurationRegistry>();
        Assert.Equal(0, configuration.GetStatus().Revision);

        var invalid = await client.PutAsJsonAsync(
            "/api/aliases/Invalid%20Key",
            new
            {
                displayName = "Invalid",
                sensorId = SimulatedSensorSource.TemperatureSensorId
            });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, configuration.GetStatus().Revision);

        var response = await client.PutAsJsonAsync(
            "/api/aliases/browser.revision",
            new
            {
                displayName = "Browser Revision",
                sensorId = SimulatedSensorSource.TemperatureSensorId
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, configuration.GetStatus().Revision);
    }

    [Fact]
    public async Task CreatesEvaluatesAndDeletesCalculatedSensorOverHttp()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            "/api/aliases/temperature.input",
            new { displayName = "Input Temperature", sensorId = SimulatedSensorSource.TemperatureSensorId })).StatusCode);

        var created = await client.PutAsJsonAsync(
            "/api/calculations/temperature.offset",
            new { displayName = "Adjusted Temperature", formula = "temperature.input + 5" });

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);

        var scaled = await client.PutAsJsonAsync(
            "/api/calculations/temperature.scaled",
            new { displayName = "Scaled Temperature", formula = "temperature.input * 2" });
        Assert.Equal(HttpStatusCode.OK, scaled.StatusCode);

        using var result = JsonDocument.Parse(
            await client.GetStringAsync("/api/sensors/by-alias/temperature.scaled"));
        Assert.Equal(60, result.RootElement.GetProperty("value").GetDouble());
        Assert.Equal("Celsius", result.RootElement.GetProperty("unit").GetString());
        Assert.Equal("calculated:temperature.scaled", result.RootElement.GetProperty("id").GetString());

        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync("/api/calculations/temperature.scaled")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/sensors/by-alias/temperature.scaled")).StatusCode);
    }

    [Fact]
    public void MalformedConfigurationPreventsStartupAndIdentifiesPreviousFile()
    {
        var configPath = Path.Combine(_root, "malformed-config.json");
        var previousPath = $"{configPath}.previous";
        File.WriteAllText(configPath, "{ malformed");
        File.WriteAllText(previousPath, """{"schemaVersion":1,"aliases":[]}""");
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["TelemetryLoom:ConfigPath"] = configPath,
                    ["Hwmon:RootPath"] = Path.Combine(_root, "missing-hwmon")
                })));

        var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(previousPath, exception.ToString(), StringComparison.Ordinal);
        Assert.Contains("restored deliberately", exception.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class ApiFixtureSensorSource : ISensorSource
    {
        private static readonly SensorReading Reading = new(
            "api-fixture:temperature:1",
            "API Fixture Temperature",
            null,
            32.5,
            QuantityKind.Temperature,
            UnitCode.Celsius,
            "°C",
            "api-fixture",
            SensorStatus.Available,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            new Dictionary<string, string>());

        public string Name => "api-fixture";
        public IReadOnlyList<SensorReading> GetSensors() => [Reading];
        public SensorReading? GetSensor(string id) =>
            string.Equals(id, Reading.Id, StringComparison.Ordinal) ? Reading : null;
    }
}
