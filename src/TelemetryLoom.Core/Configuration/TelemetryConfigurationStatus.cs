namespace TelemetryLoom.Core.Configuration;

public sealed record TelemetryConfigurationStatus(
    string ActivePath,
    int SchemaVersion,
    DateTimeOffset LoadedAt,
    long Revision,
    DateTimeOffset? LastSuccessfulSaveAt,
    string PreviousPath,
    bool PreviousExists);
