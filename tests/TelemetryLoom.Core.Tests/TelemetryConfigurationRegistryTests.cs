using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Configuration;

namespace TelemetryLoom.Core.Tests;

public sealed class TelemetryConfigurationRegistryTests
{
    [Fact]
    public void InitialStatusIsProcessLocalAndExposesStoreMetadata()
    {
        var loadedAt = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        var store = new MemoryStore();
        var registry = new TelemetryConfigurationRegistry(store, new FixedTimeProvider(loadedAt));

        var status = registry.GetStatus();

        Assert.Equal(0, status.Revision);
        Assert.Equal(loadedAt, status.LoadedAt);
        Assert.Null(status.LastSuccessfulSaveAt);
        Assert.Equal(TelemetryConfigurationDocument.CurrentSchemaVersion, status.SchemaVersion);
        Assert.Equal(store.ActivePath, status.ActivePath);
        Assert.Equal(store.PreviousPath, status.PreviousPath);
        Assert.False(status.PreviousExists);
    }

    [Fact]
    public void UpdatingEitherCollectionPreservesTheOther()
    {
        var store = new MemoryStore();
        var registry = new TelemetryConfigurationRegistry(store);
        var alias = new SensorAliasDefinition(
            "temperature.input", "Input", "fixture:temperature", QuantityKind.Temperature,
            UnitCode.Celsius, "fixture");
        var calculation = new CalculatedSensorDefinition(
            "temperature.scaled", "Scaled", "temperature.input * 2", QuantityKind.Temperature,
            UnitCode.Celsius);

        registry.UpdateAliases([alias]);
        registry.UpdateCalculatedSensors([calculation]);
        registry.UpdateAliases([alias with { DisplayName = "Renamed Input" }]);

        var saved = store.Load();
        Assert.Equal("Renamed Input", Assert.Single(saved.Aliases).DisplayName);
        Assert.Equal(calculation, Assert.Single(saved.CalculatedSensors));
        Assert.Equal(3, registry.GetStatus().Revision);
    }

    [Fact]
    public void SuccessfulWriteAdvancesRevisionAndRecordsSaveTime()
    {
        var loadedAt = DateTimeOffset.Parse("2026-08-07T12:00:00Z");
        var savedAt = loadedAt.AddMinutes(5);
        var clock = new FixedTimeProvider(loadedAt);
        var registry = new TelemetryConfigurationRegistry(new MemoryStore(), clock);
        clock.SetUtcNow(savedAt);

        registry.UpdateAliases([]);

        Assert.Equal(1, registry.GetStatus().Revision);
        Assert.Equal(savedAt, registry.GetStatus().LastSuccessfulSaveAt);
    }

    [Fact]
    public void FailedPersistenceDoesNotAdvanceRevisionOrSaveTime()
    {
        var registry = new TelemetryConfigurationRegistry(new FailingStore());

        Assert.Throws<IOException>(() => registry.UpdateAliases([]));

        Assert.Equal(0, registry.GetStatus().Revision);
        Assert.Null(registry.GetStatus().LastSuccessfulSaveAt);
    }

    [Fact]
    public void RevisionIsNotPersistedInConfigurationDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "config.json");
        Directory.CreateDirectory(root);

        try
        {
            var registry = new TelemetryConfigurationRegistry(new JsonTelemetryConfigurationStore(path));
            registry.UpdateAliases([]);

            Assert.Equal(1, registry.GetStatus().Revision);
            Assert.DoesNotContain("revision", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StatusReflectsPreviousConfigurationAvailability()
    {
        var root = Path.Combine(Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "config.json");
        Directory.CreateDirectory(root);

        try
        {
            var registry = new TelemetryConfigurationRegistry(new JsonTelemetryConfigurationStore(path));
            registry.UpdateAliases([]);
            Assert.False(registry.GetStatus().PreviousExists);

            registry.UpdateAliases([]);

            Assert.True(registry.GetStatus().PreviousExists);
            Assert.Equal($"{path}.previous", registry.GetStatus().PreviousPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class MemoryStore : ITelemetryConfigurationStore, ITelemetryConfigurationStoreMetadata
    {
        private TelemetryConfigurationDocument _document = new();
        public string ActivePath => "/memory/config.json";
        public string PreviousPath => "/memory/config.json.previous";
        public bool PreviousExists { get; set; }
        public TelemetryConfigurationDocument Load() => _document;
        public void Save(TelemetryConfigurationDocument document) => _document = document;
    }

    private sealed class FailingStore : ITelemetryConfigurationStore
    {
        public TelemetryConfigurationDocument Load() => new();
        public void Save(TelemetryConfigurationDocument document) => throw new IOException("Simulated failure.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
