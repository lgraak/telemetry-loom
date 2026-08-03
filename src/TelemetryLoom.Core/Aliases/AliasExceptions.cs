namespace TelemetryLoom.Core.Aliases;

public sealed class AliasValidationException(string message) : ArgumentException(message);

public sealed class AliasConflictException(string message) : InvalidOperationException(message);

public sealed class AliasSensorNotFoundException(string sensorId)
    : InvalidOperationException($"Sensor not found: {sensorId}")
{
    public string SensorId { get; } = sensorId;
}
