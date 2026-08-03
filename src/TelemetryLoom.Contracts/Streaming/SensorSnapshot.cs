using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Contracts.Streaming;

public sealed record SensorSnapshot(
    int SchemaVersion,
    long Sequence,
    DateTimeOffset CapturedAt,
    IReadOnlyList<SensorReading> Sensors)
{
    public const int CurrentSchemaVersion = 1;
}
