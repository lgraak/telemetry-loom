namespace TelemetryLoom.Core.Configuration;

public interface ITelemetryConfigurationStore
{
    TelemetryConfigurationDocument Load();
    void Save(TelemetryConfigurationDocument document);
}
