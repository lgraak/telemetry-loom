using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Core.Sensors;

public interface ISensorSource
{
    string Name { get; }
    IReadOnlyList<SensorReading> GetSensors();
    SensorReading? GetSensor(string id);
}
