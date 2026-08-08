using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class SensorAliasRegistryTests
{
    [Fact]
    public void AliasSurvivesRegistryRestartAndDecoratesLiveSensor()
    {
        var root = CreateTemporaryDirectory();
        var path = Path.Combine(root, "config.json");
        var source = new MutableSensorSource(CreateReading());
        var sensors = new SensorCatalog([source]);

        try
        {
            var registry = CreateRegistry(sensors, new JsonTelemetryConfigurationStore(path));
            registry.Upsert("cooling.air.intake", "Radiator Intake Air", "fixture:temperature:1");

            var reloaded = CreateRegistry(sensors, new JsonTelemetryConfigurationStore(path));
            var reading = Assert.Single(new AliasedSensorCatalog(sensors, reloaded).GetSensors());

            Assert.Equal("cooling.air.intake", reading.Alias);
            Assert.Equal("Radiator Intake Air", reading.DisplayName);
            Assert.Equal("cooling.air.intake", reading.Metadata["aliasKey"]);
            Assert.Equal(SensorStatus.Available, reading.Status);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MissingBoundSensorRemainsVisibleAndUnavailable()
    {
        var source = new MutableSensorSource(CreateReading());
        var sensors = new SensorCatalog([source]);
        var registry = CreateRegistry(sensors, new MemoryConfigurationStore());
        registry.Upsert("cooling.air.intake", "Radiator Intake Air", "fixture:temperature:1");
        source.Clear();

        var reading = new AliasedSensorCatalog(sensors, registry).GetSensorByAlias("cooling.air.intake");

        Assert.NotNull(reading);
        Assert.Equal(SensorStatus.Unavailable, reading.Status);
        Assert.Null(reading.Value);
        Assert.Equal(QuantityKind.Temperature, reading.Quantity);
        Assert.Equal(UnitCode.Celsius, reading.Unit);
        Assert.Equal("fixture:temperature:1", reading.Metadata["boundSensorId"]);
    }

    [Fact]
    public void MissingBoundSensorCanBeRenamedWithoutRebinding()
    {
        var source = new MutableSensorSource(CreateReading());
        var sensors = new SensorCatalog([source]);
        var registry = CreateRegistry(sensors, new MemoryConfigurationStore());
        registry.Upsert("cooling.air.intake", "Radiator Intake Air", "fixture:temperature:1");
        source.Clear();

        var renamed = registry.Upsert(
            "cooling.air.intake",
            "Case Intake Air",
            "fixture:temperature:1");

        Assert.Equal("Case Intake Air", renamed.DisplayName);
        Assert.Equal(SensorStatus.Unavailable, renamed.Status);
        Assert.Equal(UnitCode.Celsius, renamed.Unit);
    }

    [Fact]
    public void RejectsInvalidKeysUnknownSensorsAndDuplicateBindings()
    {
        var sensors = new SensorCatalog([new MutableSensorSource(CreateReading())]);
        var registry = CreateRegistry(sensors, new MemoryConfigurationStore());

        Assert.Throws<AliasValidationException>(() =>
            registry.Upsert("Cooling Intake", "Intake", "fixture:temperature:1"));
        Assert.Throws<AliasValidationException>(() =>
            registry.Upsert("cooling-air", "Intake", "fixture:temperature:1"));
        Assert.Throws<AliasSensorNotFoundException>(() =>
            registry.Upsert("cooling.air.missing", "Missing", "fixture:missing"));

        registry.Upsert("cooling.air.intake", "Intake", "fixture:temperature:1");

        Assert.Throws<AliasConflictException>(() =>
            registry.Upsert("cooling.air.duplicate", "Duplicate", "fixture:temperature:1"));
    }

    [Fact]
    public void DeletePersistsRemoval()
    {
        var source = new MutableSensorSource(CreateReading());
        var sensors = new SensorCatalog([source]);
        var store = new MemoryConfigurationStore();
        var registry = CreateRegistry(sensors, store);
        registry.Upsert("cooling.air.intake", "Intake", "fixture:temperature:1");

        Assert.True(registry.Delete("cooling.air.intake"));
        Assert.False(registry.Delete("cooling.air.intake"));
        Assert.Empty(store.Load().Aliases);
    }

    [Fact]
    public void FailedPersistenceDoesNotChangeInMemoryAliases()
    {
        var sensors = new SensorCatalog([new MutableSensorSource(CreateReading())]);
        var registry = CreateRegistry(sensors, new FailingConfigurationStore());

        Assert.Throws<IOException>(() =>
            registry.Upsert("cooling.air.intake", "Intake", "fixture:temperature:1"));
        Assert.Empty(registry.GetDefinitions());
    }

    [Fact]
    public void FailedReplacementLeavesActiveFileAndRegistryStateUnchanged()
    {
        var root = CreateTemporaryDirectory();
        var path = Path.Combine(root, "config.json");
        var source = new MutableSensorSource(CreateReading());
        var sensors = new SensorCatalog([source]);
        var store = new JsonTelemetryConfigurationStore(path);
        var configuration = new TelemetryConfigurationRegistry(store);
        var registry = new SensorAliasRegistry(sensors, configuration);

        try
        {
            registry.Upsert("cooling.air.intake", "Original Name", "fixture:temperature:1");
            var statusBeforeFailure = configuration.GetStatus();
            Directory.CreateDirectory(store.PreviousPath);

            var exception = Record.Exception(() =>
                registry.Upsert("cooling.air.intake", "Changed Name", "fixture:temperature:1"));

            Assert.True(
                exception is IOException or UnauthorizedAccessException,
                $"Expected a persistence exception, but received {exception?.GetType().FullName ?? "no exception"}.");
            Assert.Equal("Original Name", Assert.Single(registry.GetDefinitions()).DisplayName);
            Assert.Equal("Original Name", Assert.Single(store.Load().Aliases).DisplayName);
            Assert.Equal(statusBeforeFailure.Revision, configuration.GetStatus().Revision);
            Assert.Equal(statusBeforeFailure.LastSuccessfulSaveAt, configuration.GetStatus().LastSuccessfulSaveAt);
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static SensorReading CreateReading() =>
        new(
            "fixture:temperature:1",
            "Fixture Temperature",
            null,
            30,
            QuantityKind.Temperature,
            UnitCode.Celsius,
            "°C",
            "fixture",
            SensorStatus.Available,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            new Dictionary<string, string>());

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class MutableSensorSource(params SensorReading[] sensors) : ISensorSource
    {
        private readonly List<SensorReading> _sensors = [.. sensors];

        public string Name => "fixture";
        public IReadOnlyList<SensorReading> GetSensors() => [.. _sensors];
        public SensorReading? GetSensor(string id) =>
            _sensors.FirstOrDefault(sensor => string.Equals(sensor.Id, id, StringComparison.Ordinal));
        public void Clear() => _sensors.Clear();
    }

    private static SensorAliasRegistry CreateRegistry(SensorCatalog sensors, ITelemetryConfigurationStore store) =>
        new(sensors, new TelemetryConfigurationRegistry(store));

    private sealed class MemoryConfigurationStore : ITelemetryConfigurationStore
    {
        private TelemetryConfigurationDocument _document = new();

        public TelemetryConfigurationDocument Load() => _document;

        public void Save(TelemetryConfigurationDocument document) => _document = document;
    }

    private sealed class FailingConfigurationStore : ITelemetryConfigurationStore
    {
        public TelemetryConfigurationDocument Load() => new();

        public void Save(TelemetryConfigurationDocument document) =>
            throw new IOException("Simulated persistence failure.");
    }
}
