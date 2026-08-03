using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Presentation;

namespace TelemetryLoom.Core.Sensors;

public sealed class EnrichedSensorCatalog(
    SensorCatalog normalizedSensors,
    SensorPresentationRegistry presentation) : ISensorCatalog
{
    public IReadOnlyList<string> SourceNames => normalizedSensors.SourceNames;

    public IReadOnlyList<SensorReading> GetSensors() =>
        [.. normalizedSensors.GetSensors().Select(presentation.Enrich)];

    public SensorReading? GetSensor(string id) =>
        normalizedSensors.GetSensor(id) is { } sensor ? presentation.Enrich(sensor) : null;
}
