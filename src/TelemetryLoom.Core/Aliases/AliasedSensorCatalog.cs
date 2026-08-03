using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Units;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Aliases;

public sealed class AliasedSensorCatalog(
    ISensorCatalog sensorCatalog,
    SensorAliasRegistry aliasRegistry)
{
    public IReadOnlyList<string> SourceNames => sensorCatalog.SourceNames;

    public IReadOnlyList<SensorReading> GetSensors()
    {
        var aliases = aliasRegistry.GetDefinitions();
        var aliasesBySensorId = aliases.ToDictionary(alias => alias.SensorId, StringComparer.Ordinal);
        var seenSensorIds = new HashSet<string>(StringComparer.Ordinal);
        var sensors = new List<SensorReading>();

        foreach (var sensor in sensorCatalog.GetSensors())
        {
            seenSensorIds.Add(sensor.Id);
            sensors.Add(aliasesBySensorId.TryGetValue(sensor.Id, out var alias)
                ? ApplyAlias(sensor, alias)
                : sensor);
        }

        sensors.AddRange(aliases
            .Where(alias => !seenSensorIds.Contains(alias.SensorId))
            .Select(CreateUnavailableReading));

        return [.. sensors.OrderBy(sensor => sensor.Id, StringComparer.Ordinal)];
    }

    public SensorReading? GetSensor(string id)
    {
        var alias = aliasRegistry.GetDefinitions()
            .FirstOrDefault(candidate => string.Equals(candidate.SensorId, id, StringComparison.Ordinal));
        var sensor = sensorCatalog.GetSensor(id);

        if (sensor is not null)
        {
            return alias is null ? sensor : ApplyAlias(sensor, alias);
        }

        return alias is null ? null : CreateUnavailableReading(alias);
    }

    public SensorReading? GetSensorByAlias(string key)
    {
        var alias = aliasRegistry.GetDefinitions()
            .FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.Ordinal));
        return alias is null ? null : GetSensor(alias.SensorId);
    }

    private static SensorReading ApplyAlias(SensorReading sensor, SensorAliasDefinition alias) =>
        sensor with
        {
            DisplayName = alias.DisplayName,
            Alias = alias.Key,
            Metadata = WithAliasMetadata(sensor.Metadata, alias.Key),
            Presentation = ApplyAliasPresentation(sensor, alias)
        };

    private static SensorReading CreateUnavailableReading(SensorAliasDefinition alias) =>
        new(
            alias.SensorId,
            alias.DisplayName,
            alias.Key,
            null,
            alias.Quantity,
            alias.Unit,
            UnitCatalog.Get(alias.Unit).Symbol,
            alias.Source,
            SensorStatus.Unavailable,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["aliasKey"] = alias.Key,
                ["boundSensorId"] = alias.SensorId,
                ["missingReason"] = "Bound sensor is not currently available."
            },
            new SensorPresentation(
                alias.DisplayName,
                alias.DisplayName,
                "User-defined alias whose bound sensor is not currently available.",
                "Other",
                "Unavailable Sensors",
                "alias:unavailable",
                alias.Quantity.ToString(),
                InterpretationConfidence.UserDefined,
                null));

    private static SensorPresentation ApplyAliasPresentation(
        SensorReading sensor,
        SensorAliasDefinition alias)
    {
        var existing = sensor.Presentation;
        return existing is null
            ? new SensorPresentation(
                sensor.DisplayName,
                alias.DisplayName,
                "User-defined sensor alias.",
                "Other",
                sensor.Source,
                $"{sensor.Source}:default",
                sensor.Quantity.ToString(),
                InterpretationConfidence.UserDefined,
                null)
            : existing with
            {
                DisplayName = alias.DisplayName,
                InterpretationConfidence = InterpretationConfidence.UserDefined
            };
    }

    private static IReadOnlyDictionary<string, string> WithAliasMetadata(
        IReadOnlyDictionary<string, string> metadata,
        string aliasKey)
    {
        var decorated = new Dictionary<string, string>(metadata, StringComparer.Ordinal)
        {
            ["aliasKey"] = aliasKey
        };
        return decorated;
    }
}
