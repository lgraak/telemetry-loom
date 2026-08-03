namespace TelemetryLoom.Contracts.Sensors;

public sealed record SensorReading(
    string Id,
    string DisplayName,
    string? Alias,
    double? Value,
    QuantityKind Quantity,
    UnitCode Unit,
    string UnitSymbol,
    string Source,
    SensorStatus Status,
    DateTimeOffset? LastSuccessfulUpdate,
    IReadOnlyDictionary<string, string> Metadata,
    SensorPresentation? Presentation = null);
