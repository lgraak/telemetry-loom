using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Contracts.Aliases;

public sealed record ResolvedSensorAlias(
    string Key,
    string DisplayName,
    string SensorId,
    QuantityKind Quantity,
    UnitCode Unit,
    string UnitSymbol,
    string Source,
    SensorStatus Status);
