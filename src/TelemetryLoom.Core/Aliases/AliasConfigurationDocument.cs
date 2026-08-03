using TelemetryLoom.Contracts.Aliases;

namespace TelemetryLoom.Core.Aliases;

public sealed record AliasConfigurationDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public List<SensorAliasDefinition> Aliases { get; init; } = [];
}
