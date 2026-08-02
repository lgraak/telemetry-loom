using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Contracts.Units;

public sealed record UnitDefinition(UnitCode Code, QuantityKind Quantity, string Symbol);
