using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Core.Sensors;

public sealed class SensorCatalog(IEnumerable<ISensorSource> sources)
{
    private readonly IReadOnlyList<ISensorSource> _sources = [.. sources];

    public IReadOnlyList<SensorReading> GetSensors() =>
        [.. _sources.SelectMany(source => source.GetSensors()).OrderBy(sensor => sensor.Id, StringComparer.Ordinal)];

    public SensorReading? GetSensor(string id)
    {
        foreach (var source in _sources)
        {
            if (source.GetSensor(id) is { } sensor)
            {
                return sensor;
            }
        }

        return null;
    }
}
