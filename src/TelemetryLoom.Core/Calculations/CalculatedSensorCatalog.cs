using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Units;
using TelemetryLoom.Core.Aliases;

namespace TelemetryLoom.Core.Calculations;

public sealed class CalculatedSensorCatalog(
    AliasedSensorCatalog physicalSensors,
    CalculatedSensorRegistry registry)
{
    public IReadOnlyList<SensorReading> GetSensors()
    {
        var cache = new Dictionary<string, SensorReading>(StringComparer.Ordinal);
        return
        [
            .. registry.GetDefinitions()
                .Select(definition => Evaluate(definition, cache, new HashSet<string>(StringComparer.Ordinal)))
                .OrderBy(sensor => sensor.Id, StringComparer.Ordinal)
        ];
    }

    public SensorReading? GetSensor(string id)
    {
        const string prefix = "calculated:";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return null;
        return GetSensorByAlias(id[prefix.Length..]);
    }

    public SensorReading? GetSensorByAlias(string key)
    {
        var definition = registry.GetDefinition(key);
        return definition is null
            ? null
            : Evaluate(definition, new Dictionary<string, SensorReading>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
    }

    private SensorReading Evaluate(
        CalculatedSensorDefinition definition,
        IDictionary<string, SensorReading> cache,
        ISet<string> visiting)
    {
        if (cache.TryGetValue(definition.Key, out var cached)) return cached;
        if (!visiting.Add(definition.Key))
            return CreateReading(definition, null, SensorStatus.CalculationError, null, [], "A calculated sensor dependency cycle was encountered.");

        var evaluatedAt = DateTimeOffset.UtcNow;
        try
        {
            var expression = FormulaEngine.Parse(definition.Formula);
            var dependencyKeys = FormulaEngine.GetDependencies(expression);
            var dependencies = dependencyKeys.Select(Resolve).ToArray();
            var blocking = SelectBlockingStatus(dependencies);
            SensorReading result;

            if (blocking is not null)
            {
                result = CreateReading(
                    definition,
                    null,
                    blocking.Value,
                    null,
                    dependencyKeys,
                    DescribeBlocking(dependencyKeys, dependencies, blocking.Value),
                    evaluatedAt);
            }
            else
            {
                try
                {
                    var byKey = dependencyKeys.Zip(dependencies).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal);
                    var value = FormulaEngine.Evaluate(expression, key =>
                    {
                        var sensor = byKey[key];
                        return new FormulaValue(sensor.Value!.Value, new FormulaType(sensor.Quantity, sensor.Unit));
                    });
                    var configuredType = new FormulaType(definition.Quantity, definition.Unit);
                    if (value.Type != configuredType)
                        throw new FormulaException(
                            $"Formula result type changed from {configuredType.Quantity}/{configuredType.Unit} " +
                            $"to {value.Type.Quantity}/{value.Type.Unit}; update the definition after rebinding its dependencies");
                    var oldest = dependencies.Length == 0
                        ? evaluatedAt
                        : dependencies.Select(sensor => sensor.LastSuccessfulUpdate).Min();
                    result = CreateReading(definition, value.Value, SensorStatus.Available, oldest, dependencyKeys, null, evaluatedAt);
                }
                catch (FormulaException exception)
                {
                    result = CreateReading(definition, null, SensorStatus.CalculationError, null, dependencyKeys, exception.Message, evaluatedAt);
                }
            }

            cache[definition.Key] = result;
            return result;

            SensorReading Resolve(string key)
            {
                var physical = physicalSensors.GetSensorByAlias(key);
                if (physical is not null) return physical;
                var calculated = registry.GetDefinition(key);
                if (calculated is not null) return Evaluate(calculated, cache, visiting);
                return new SensorReading(
                    $"missing:{key}", key, key, null, QuantityKind.Unknown, UnitCode.Unknown, string.Empty,
                    "calculated", SensorStatus.MissingDependency, null,
                    new Dictionary<string, string> { ["missingAlias"] = key });
            }
        }
        catch (FormulaException exception)
        {
            var failed = CreateReading(definition, null, SensorStatus.CalculationError, null, [], exception.Message, evaluatedAt);
            cache[definition.Key] = failed;
            return failed;
        }
        finally { visiting.Remove(definition.Key); }
    }

    private static SensorStatus? SelectBlockingStatus(IReadOnlyCollection<SensorReading> dependencies)
    {
        if (dependencies.Any(sensor => sensor.Status == SensorStatus.CalculationError)) return SensorStatus.CalculationError;
        if (dependencies.Any(sensor => sensor.Status == SensorStatus.MissingDependency)) return SensorStatus.MissingDependency;
        if (dependencies.Any(sensor => sensor.Status == SensorStatus.Unavailable || sensor.Value is null || !double.IsFinite(sensor.Value.Value))) return SensorStatus.Unavailable;
        if (dependencies.Any(sensor => sensor.Status == SensorStatus.Stale)) return SensorStatus.Stale;
        return null;
    }

    private static string DescribeBlocking(
        IReadOnlyList<string> keys,
        IReadOnlyList<SensorReading> readings,
        SensorStatus status)
    {
        var affected = keys.Zip(readings)
            .Where(pair => Matches(pair.Second, status))
            .Select(pair => pair.First);
        return $"{status}: {string.Join(", ", affected)}";

        static bool Matches(SensorReading reading, SensorStatus expected) => expected switch
        {
            SensorStatus.Unavailable => reading.Status == SensorStatus.Unavailable || reading.Value is null || (reading.Value is not null && !double.IsFinite(reading.Value.Value)),
            _ => reading.Status == expected
        };
    }

    private static SensorReading CreateReading(
        CalculatedSensorDefinition definition,
        double? value,
        SensorStatus status,
        DateTimeOffset? lastSuccessfulUpdate,
        IReadOnlyList<string> dependencies,
        string? error,
        DateTimeOffset? evaluatedAt = null)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["formula"] = definition.Formula,
            ["dependencies"] = string.Join(",", dependencies),
            ["evaluatedAt"] = (evaluatedAt ?? DateTimeOffset.UtcNow).ToString("O")
        };
        if (error is not null) metadata["error"] = error;

        return new SensorReading(
            $"calculated:{definition.Key}", definition.DisplayName, definition.Key, value,
            definition.Quantity, definition.Unit, UnitCatalog.Get(definition.Unit).Symbol,
            "calculated", status, lastSuccessfulUpdate, metadata);
    }
}
