using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Contracts.Calculations;

public sealed record CalculatedSensorDefinition(
    string Key,
    string DisplayName,
    string Formula,
    QuantityKind Quantity,
    UnitCode Unit);
