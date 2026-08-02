namespace TelemetryLoom.Contracts.Sensors;

public enum SensorStatus
{
    Available,
    Unavailable,
    Stale,
    CalculationError,
    MissingDependency
}
