using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Core.Sensors;

public interface ISensorCatalog
{
    IReadOnlyList<string> SourceNames { get; }
    IReadOnlyList<SensorReading> GetSensors();
    SensorReading? GetSensor(string id);
}
