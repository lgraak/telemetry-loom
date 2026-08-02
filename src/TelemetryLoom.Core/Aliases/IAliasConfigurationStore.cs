using TelemetryLoom.Contracts.Aliases;

namespace TelemetryLoom.Core.Aliases;

public interface IAliasConfigurationStore
{
    IReadOnlyList<SensorAliasDefinition> Load();
    void Save(IReadOnlyCollection<SensorAliasDefinition> aliases);
}
