using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Contracts.Aliases;

public sealed record SensorAliasDefinition(
    string Key,
    string DisplayName,
    string SensorId,
    QuantityKind Quantity,
    UnitCode Unit,
    string Source);
