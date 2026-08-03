using System.Text.RegularExpressions;
using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Units;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Aliases;

public sealed partial class SensorAliasRegistry
{
    private readonly object _gate = new();
    private readonly SensorCatalog _sensorCatalog;
    private readonly IAliasConfigurationStore _store;
    private IReadOnlyList<SensorAliasDefinition> _aliases;

    public SensorAliasRegistry(SensorCatalog sensorCatalog, IAliasConfigurationStore store)
    {
        _sensorCatalog = sensorCatalog;
        _store = store;
        _aliases = ValidateLoadedAliases(store.Load());
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:[._-][a-z0-9]+)*$")]
    private static partial Regex AliasKeyRegex();

    public IReadOnlyList<SensorAliasDefinition> GetDefinitions()
    {
        lock (_gate)
        {
            return [.. _aliases];
        }
    }

    public IReadOnlyList<ResolvedSensorAlias> GetAliases()
    {
        var aliases = GetDefinitions();
        var sensors = _sensorCatalog.GetSensors()
            .ToDictionary(sensor => sensor.Id, StringComparer.Ordinal);

        return
        [
            .. aliases.Select(alias => ToResolved(
                    alias,
                    sensors.TryGetValue(alias.SensorId, out var sensor)
                        ? sensor.Status
                        : SensorStatus.Unavailable))
                .OrderBy(alias => alias.Key, StringComparer.Ordinal)
        ];
    }

    public ResolvedSensorAlias? GetAlias(string key)
    {
        var definition = GetDefinitions()
            .FirstOrDefault(alias => string.Equals(alias.Key, key, StringComparison.Ordinal));
        if (definition is null)
        {
            return null;
        }

        var status = _sensorCatalog.GetSensor(definition.SensorId)?.Status ?? SensorStatus.Unavailable;
        return ToResolved(definition, status);
    }

    public ResolvedSensorAlias Upsert(string key, string displayName, string sensorId)
    {
        ValidateKey(key);
        var normalizedDisplayName = ValidateDisplayName(displayName);
        var normalizedSensorId = ValidateSensorId(sensorId);
        var sensor = _sensorCatalog.GetSensor(normalizedSensorId);

        lock (_gate)
        {
            var existing = _aliases.FirstOrDefault(alias =>
                string.Equals(alias.Key, key, StringComparison.Ordinal));
            if (sensor is null &&
                (existing is null || !string.Equals(existing.SensorId, normalizedSensorId, StringComparison.Ordinal)))
            {
                throw new AliasSensorNotFoundException(normalizedSensorId);
            }

            if (_aliases.Any(alias =>
                    string.Equals(alias.SensorId, normalizedSensorId, StringComparison.Ordinal) &&
                    !string.Equals(alias.Key, key, StringComparison.Ordinal)))
            {
                throw new AliasConflictException($"Sensor already has an alias: {normalizedSensorId}");
            }

            var definition = sensor is null
                ? existing! with { DisplayName = normalizedDisplayName }
                : new SensorAliasDefinition(
                    key,
                    normalizedDisplayName,
                    normalizedSensorId,
                    sensor.Quantity,
                    sensor.Unit,
                    sensor.Source);
            var next = _aliases
                .Where(alias => !string.Equals(alias.Key, key, StringComparison.Ordinal))
                .Append(definition)
                .OrderBy(alias => alias.Key, StringComparer.Ordinal)
                .ToArray();

            _store.Save(next);
            _aliases = next;
            return ToResolved(definition, sensor?.Status ?? SensorStatus.Unavailable);
        }
    }

    public bool Delete(string key)
    {
        lock (_gate)
        {
            var next = _aliases
                .Where(alias => !string.Equals(alias.Key, key, StringComparison.Ordinal))
                .ToArray();
            if (next.Length == _aliases.Count)
            {
                return false;
            }

            _store.Save(next);
            _aliases = next;
            return true;
        }
    }

    private static IReadOnlyList<SensorAliasDefinition> ValidateLoadedAliases(
        IReadOnlyList<SensorAliasDefinition> aliases)
    {
        try
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var sensorIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var alias in aliases)
            {
                ValidateKey(alias.Key);
                ValidateDisplayName(alias.DisplayName);
                ValidateSensorId(alias.SensorId);
                ValidateSnapshotMetadata(alias);

                if (!keys.Add(alias.Key))
                {
                    throw new AliasConflictException($"Duplicate alias key: {alias.Key}");
                }

                if (!sensorIds.Add(alias.SensorId))
                {
                    throw new AliasConflictException($"Multiple aliases target sensor: {alias.SensorId}");
                }
            }

            return [.. aliases.OrderBy(alias => alias.Key, StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is AliasValidationException or AliasConflictException)
        {
            throw new InvalidDataException($"Invalid alias configuration: {exception.Message}", exception);
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || !AliasKeyRegex().IsMatch(key))
        {
            throw new AliasValidationException(
                "Alias key must be 1-128 lowercase characters, start with a letter, and use alphanumeric segments separated by '.', '_', or '-'.");
        }
    }

    private static string ValidateDisplayName(string displayName)
    {
        var normalized = displayName?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 256)
        {
            throw new AliasValidationException("Display name must be 1-256 characters.");
        }

        return normalized;
    }

    private static string ValidateSensorId(string sensorId)
    {
        var normalized = sensorId?.Trim() ?? string.Empty;
        if (normalized.Length is 0 or > 512)
        {
            throw new AliasValidationException("Sensor ID must be 1-512 characters.");
        }

        return normalized;
    }

    private static void ValidateSnapshotMetadata(SensorAliasDefinition alias)
    {
        if (!Enum.IsDefined(alias.Quantity) || !Enum.IsDefined(alias.Unit))
        {
            throw new AliasValidationException($"Alias '{alias.Key}' has an unknown quantity or unit.");
        }

        var unit = UnitCatalog.Get(alias.Unit);
        if (unit.Quantity != alias.Quantity)
        {
            throw new AliasValidationException($"Alias '{alias.Key}' has inconsistent unit metadata.");
        }

        if (string.IsNullOrWhiteSpace(alias.Source) || alias.Source.Length > 128)
        {
            throw new AliasValidationException($"Alias '{alias.Key}' has an invalid source name.");
        }
    }

    private static ResolvedSensorAlias ToResolved(SensorAliasDefinition alias, SensorStatus status) =>
        new(
            alias.Key,
            alias.DisplayName,
            alias.SensorId,
            alias.Quantity,
            alias.Unit,
            UnitCatalog.Get(alias.Unit).Symbol,
            alias.Source,
            status);
}
