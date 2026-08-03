namespace TelemetryLoom.Contracts.Sensors;

public sealed record SensorPresentation(
    string RawLabel,
    string DisplayName,
    string Description,
    string DeviceCategory,
    string DeviceDisplayName,
    string DeviceGroupKey,
    string MetricCategory,
    InterpretationConfidence InterpretationConfidence,
    string? DocumentationKey);
