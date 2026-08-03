using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Calculations;

namespace TelemetryLoom.Core.Configuration;

public sealed record TelemetryConfigurationDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public List<SensorAliasDefinition> Aliases { get; init; } = [];
    public List<CalculatedSensorDefinition> CalculatedSensors { get; init; } = [];
}
