using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Units;

namespace TelemetryLoom.Core.Sensors;

public sealed class SimulatedSensorSource(TimeProvider timeProvider) : ISensorSource
{
    public const string TemperatureSensorId = "simulated:temperature:1";

    public SimulatedSensorSource() : this(TimeProvider.System)
    {
    }

    public string Name => "simulated";

    public IReadOnlyList<SensorReading> GetSensors() => [CreateTemperatureReading()];

    public SensorReading? GetSensor(string id) =>
        string.Equals(id, TemperatureSensorId, StringComparison.Ordinal)
            ? CreateTemperatureReading()
            : null;

    private SensorReading CreateTemperatureReading()
    {
        var unit = UnitCatalog.Get(UnitCode.Celsius);

        return new SensorReading(
            TemperatureSensorId,
            "Simulated Temperature",
            null,
            30.0,
            unit.Quantity,
            unit.Code,
            unit.Symbol,
            Name,
            SensorStatus.Available,
            timeProvider.GetUtcNow(),
            new Dictionary<string, string>
            {
                ["collector"] = Name,
                ["purpose"] = "milestone-1"
            });
    }
}
