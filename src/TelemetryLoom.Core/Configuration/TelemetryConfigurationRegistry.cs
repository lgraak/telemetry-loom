using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Calculations;

namespace TelemetryLoom.Core.Configuration;

public sealed class TelemetryConfigurationRegistry
{
    private readonly object _gate = new();
    private readonly ITelemetryConfigurationStore _store;
    private readonly ITelemetryConfigurationStoreMetadata? _storeMetadata;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _loadedAt;
    private TelemetryConfigurationDocument _document;
    private long _revision;
    private DateTimeOffset? _lastSuccessfulSaveAt;

    public TelemetryConfigurationRegistry(ITelemetryConfigurationStore store)
        : this(store, TimeProvider.System)
    {
    }

    public TelemetryConfigurationRegistry(
        ITelemetryConfigurationStore store,
        TimeProvider timeProvider)
    {
        _store = store;
        _storeMetadata = store as ITelemetryConfigurationStoreMetadata;
        _timeProvider = timeProvider;
        _document = Clone(store.Load());
        _loadedAt = timeProvider.GetUtcNow();
    }

    public TelemetryConfigurationDocument GetSnapshot()
    {
        lock (_gate)
        {
            return Clone(_document);
        }
    }

    public TelemetryConfigurationStatus GetStatus()
    {
        lock (_gate)
        {
            return new TelemetryConfigurationStatus(
                _storeMetadata?.ActivePath ?? "Unavailable",
                _document.SchemaVersion,
                _loadedAt,
                _revision,
                _lastSuccessfulSaveAt,
                _storeMetadata?.PreviousPath ?? "Unavailable",
                _storeMetadata?.PreviousExists ?? false);
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
            _store.Save(next);
            _document = Clone(next);
            RecordSuccessfulSave();
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
            _store.Save(next);
            _document = Clone(next);
            RecordSuccessfulSave();
        }
    }

    private void RecordSuccessfulSave()
    {
        _revision++;
        _lastSuccessfulSaveAt = _timeProvider.GetUtcNow();
    }

    private static TelemetryConfigurationDocument Clone(TelemetryConfigurationDocument document) =>
        document with
        {
            Aliases = [.. document.Aliases],
            CalculatedSensors = [.. document.CalculatedSensors]
        };
}
