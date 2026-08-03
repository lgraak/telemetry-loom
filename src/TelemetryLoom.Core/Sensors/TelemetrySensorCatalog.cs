using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;

namespace TelemetryLoom.Core.Sensors;

public sealed class TelemetrySensorCatalog(
    AliasedSensorCatalog physicalSensors,
    CalculatedSensorCatalog calculatedSensors)
{
    public IReadOnlyList<string> SourceNames => [.. physicalSensors.SourceNames.Append("calculated").Distinct(StringComparer.Ordinal)];

    public IReadOnlyList<SensorReading> GetSensors() =>
        [.. physicalSensors.GetSensors().Concat(calculatedSensors.GetSensors()).OrderBy(sensor => sensor.Id, StringComparer.Ordinal)];

    public SensorReading? GetSensor(string id) => physicalSensors.GetSensor(id) ?? calculatedSensors.GetSensor(id);

    public SensorReading? GetSensorByAlias(string key) =>
        physicalSensors.GetSensorByAlias(key) ?? calculatedSensors.GetSensorByAlias(key);
}
