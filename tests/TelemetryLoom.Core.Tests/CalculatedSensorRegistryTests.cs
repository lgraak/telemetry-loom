using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class CalculatedSensorRegistryTests
{
    [Fact]
    public void ValidateDoesNotWriteAdvanceRevisionOrMutateDefinitions()
    {
        var root = CreateTemporaryDirectory();
        var path = Path.Combine(root, "config.json");
        var fixture = new RegistryFixture(new JsonTelemetryConfigurationStore(path));

        try
        {
            fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
            var fileBefore = File.ReadAllText(path);
            var statusBefore = fixture.Configuration.GetStatus();

            var validation = fixture.Registry.Validate(
                "temperature.scaled", "Scaled", "temperature.input * 2");

            Assert.Equal(QuantityKind.Temperature, validation.Definition.Quantity);
            Assert.Equal(UnitCode.Celsius, validation.Definition.Unit);
            Assert.Equal(["temperature.input"], validation.Dependencies);
            Assert.Equal(fileBefore, File.ReadAllText(path));
            Assert.Equal(statusBefore, fixture.Configuration.GetStatus());
            Assert.Empty(fixture.Registry.GetDefinitions());
            Assert.False(File.Exists($"{path}.previous"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ConditionalUpsertAndDeleteAtCurrentRevisionPersist()
    {
        var fixture = new RegistryFixture(new MemoryStore());
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");

        var definition = fixture.Registry.Upsert(
            "temperature.scaled", "Scaled", "temperature.input * 2", expectedRevision: 1);

        Assert.Equal("temperature.scaled", definition.Key);
        Assert.Equal(2, fixture.Configuration.GetStatus().Revision);
        Assert.True(fixture.Registry.Delete("temperature.scaled", expectedRevision: 2));
        Assert.Equal(3, fixture.Configuration.GetStatus().Revision);
        Assert.Empty(fixture.Registry.GetDefinitions());
    }

    [Fact]
    public void StaleConditionalUpsertAndDeleteLeaveDiskAndDefinitionsUnchanged()
    {
        var root = CreateTemporaryDirectory();
        var path = Path.Combine(root, "config.json");
        var fixture = new RegistryFixture(new JsonTelemetryConfigurationStore(path));

        try
        {
            fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
            fixture.Registry.Upsert("temperature.scaled", "Original", "temperature.input * 2");
            var fileBefore = File.ReadAllText(path);
            var definitionsBefore = fixture.Registry.GetDefinitions();
            var statusBefore = fixture.Configuration.GetStatus();

            Assert.Throws<ConfigurationRevisionConflictException>(() =>
                fixture.Registry.Upsert(
                    "temperature.scaled", "Replacement", "temperature.input * 3", expectedRevision: 1));
            Assert.Throws<ConfigurationRevisionConflictException>(() =>
                fixture.Registry.Delete("temperature.scaled", expectedRevision: 1));

            Assert.Equal(fileBefore, File.ReadAllText(path));
            Assert.Equal(definitionsBefore, fixture.Registry.GetDefinitions());
            Assert.Equal(statusBefore, fixture.Configuration.GetStatus());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RegistryFixture
    {
        private readonly SensorCatalog _sensors = new([new FixtureSource()]);

        public RegistryFixture(ITelemetryConfigurationStore store)
        {
            Configuration = new TelemetryConfigurationRegistry(store);
            Aliases = new SensorAliasRegistry(_sensors, Configuration);
            Registry = new CalculatedSensorRegistry(Configuration, Aliases);
        }

        public TelemetryConfigurationRegistry Configuration { get; }
        public SensorAliasRegistry Aliases { get; }
        public CalculatedSensorRegistry Registry { get; }
    }

    private sealed class FixtureSource : ISensorSource
    {
        private static readonly SensorReading Reading = new(
            "fixture:temperature",
            "Temperature",
            null,
            20,
            QuantityKind.Temperature,
            UnitCode.Celsius,
            "°C",
            "fixture",
            SensorStatus.Available,
            DateTimeOffset.Parse("2026-08-07T12:00:00Z"),
            new Dictionary<string, string>());

        public string Name => "fixture";
        public IReadOnlyList<SensorReading> GetSensors() => [Reading];
        public SensorReading? GetSensor(string id) => id == Reading.Id ? Reading : null;
    }

    private sealed class MemoryStore : ITelemetryConfigurationStore
    {
        private TelemetryConfigurationDocument _document = new();
        public TelemetryConfigurationDocument Load() => _document;
        public void Save(TelemetryConfigurationDocument document) => _document = document;
    }
}
