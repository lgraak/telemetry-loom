using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Configuration;

namespace TelemetryLoom.Core.Tests;

public sealed class TelemetryConfigurationRegistryTests
{
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
    }

    private sealed class MemoryStore : ITelemetryConfigurationStore
    {
        private TelemetryConfigurationDocument _document = new();
        public TelemetryConfigurationDocument Load() => _document;
        public void Save(TelemetryConfigurationDocument document) => _document = document;
    }
}
