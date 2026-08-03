using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Calculations;

namespace TelemetryLoom.Core.Configuration;

public sealed class TelemetryConfigurationRegistry(ITelemetryConfigurationStore store)
{
    private readonly object _gate = new();
    private TelemetryConfigurationDocument _document = Clone(store.Load());

    public TelemetryConfigurationDocument GetSnapshot()
    {
        lock (_gate)
        {
            return Clone(_document);
        }
    }

    public void UpdateAliases(IReadOnlyCollection<SensorAliasDefinition> aliases)
    {
        lock (_gate)
        {
            var next = _document with
            {
                Aliases = [.. aliases.OrderBy(alias => alias.Key, StringComparer.Ordinal)],
                CalculatedSensors = [.. _document.CalculatedSensors]
            };
            store.Save(next);
            _document = Clone(next);
        }
    }

    public void UpdateCalculatedSensors(IReadOnlyCollection<CalculatedSensorDefinition> calculatedSensors)
    {
        lock (_gate)
        {
            var next = _document with
            {
                Aliases = [.. _document.Aliases],
                CalculatedSensors = [.. calculatedSensors.OrderBy(sensor => sensor.Key, StringComparer.Ordinal)]
            };
            store.Save(next);
            _document = Clone(next);
        }
    }

    private static TelemetryConfigurationDocument Clone(TelemetryConfigurationDocument document) =>
        document with
        {
            Aliases = [.. document.Aliases],
            CalculatedSensors = [.. document.CalculatedSensors]
        };
}
