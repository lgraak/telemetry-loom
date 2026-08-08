namespace TelemetryLoom.Core.Configuration;

public interface ITelemetryConfigurationStoreMetadata
{
    string ActivePath { get; }
    string PreviousPath { get; }
    bool PreviousExists { get; }
}
