using System.Text.RegularExpressions;
using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Units;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Configuration;

namespace TelemetryLoom.Core.Calculations;

public sealed partial class CalculatedSensorRegistry
{
    private readonly object _gate = new();
    private readonly TelemetryConfigurationRegistry _configuration;
    private readonly SensorAliasRegistry _aliases;
    private IReadOnlyList<CalculatedSensorDefinition> _definitions;

    public CalculatedSensorRegistry(TelemetryConfigurationRegistry configuration, SensorAliasRegistry aliases)
    {
        _configuration = configuration;
        _aliases = aliases;
        _definitions = ValidateLoaded(configuration.GetSnapshot().CalculatedSensors);
    }

    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:[._][a-z0-9]+)*$")]
    private static partial Regex KeyRegex();

    public IReadOnlyList<CalculatedSensorDefinition> GetDefinitions()
    {
        lock (_gate) return [.. _definitions];
    }

    public CalculatedSensorDefinition? GetDefinition(string key) => GetDefinitions()
        .FirstOrDefault(definition => string.Equals(definition.Key, key, StringComparison.Ordinal));

    public IReadOnlyList<CalculatedSensorDefinition> GetDirectDependents(string key)
    {
        lock (_gate)
        {
            return
            [
                .. _definitions
                    .Where(definition => FormulaEngine
                        .GetDependencies(FormulaEngine.Parse(definition.Formula))
                        .Contains(key, StringComparer.Ordinal))
                    .OrderBy(definition => definition.Key, StringComparer.Ordinal)
            ];
        }
    }

    public CalculatedSensorDefinition Upsert(string key, string displayName, string formula)
    {
        ValidateKey(key);
        var name = ValidateDisplayName(displayName);
        var normalizedFormula = ValidateFormulaText(formula);

        lock (_gate)
        {
            if (_aliases.GetDefinitions().Any(alias => string.Equals(alias.Key, key, StringComparison.Ordinal)))
                throw new CalculationConflictException($"A physical sensor alias already uses key: {key}");

            FormulaExpression expression;
            try { expression = FormulaEngine.Parse(normalizedFormula); }
            catch (FormulaException exception) { throw new CalculationValidationException(exception.Message); }

            var candidates = _definitions
                .Where(definition => !string.Equals(definition.Key, key, StringComparison.Ordinal))
                .Append(new CalculatedSensorDefinition(key, name, normalizedFormula, QuantityKind.Unknown, UnitCode.Unknown))
                .ToDictionary(definition => definition.Key, StringComparer.Ordinal);
            EnsureAcyclic(candidates);

            FormulaType type;
            try
            {
                type = InferDefinition(key, candidates, new HashSet<string>(StringComparer.Ordinal));
            }
            catch (FormulaException exception)
            {
                throw new CalculationValidationException(exception.Message);
            }

            var definition = new CalculatedSensorDefinition(key, name, normalizedFormula, type.Quantity, type.Unit);
            candidates[key] = definition;
            var next = candidates.Values.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
            _configuration.UpdateCalculatedSensors(next);
            _definitions = next;
            return definition;
        }
    }

    public bool Delete(string key)
    {
        lock (_gate)
        {
            var next = _definitions.Where(item => !string.Equals(item.Key, key, StringComparison.Ordinal)).ToArray();
            if (next.Length == _definitions.Count) return false;
            _configuration.UpdateCalculatedSensors(next);
            _definitions = next;
            return true;
        }
    }

    private IReadOnlyList<CalculatedSensorDefinition> ValidateLoaded(IReadOnlyList<CalculatedSensorDefinition> definitions)
    {
        try
        {
            var byKey = new Dictionary<string, CalculatedSensorDefinition>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                ValidateKey(definition.Key);
                ValidateDisplayName(definition.DisplayName);
                ValidateFormulaText(definition.Formula);
                _ = FormulaEngine.Parse(definition.Formula);
                if (!Enum.IsDefined(definition.Quantity) || !Enum.IsDefined(definition.Unit))
                    throw new CalculationValidationException($"Calculated sensor '{definition.Key}' has unknown unit metadata.");
                if (definition.Quantity is QuantityKind.Unknown || definition.Unit is UnitCode.Unknown ||
                    UnitCatalog.Get(definition.Unit).Quantity != definition.Quantity)
                    throw new CalculationValidationException($"Calculated sensor '{definition.Key}' has inconsistent unit metadata.");
                if (_aliases.GetDefinitions().Any(alias => string.Equals(alias.Key, definition.Key, StringComparison.Ordinal)))
                    throw new CalculationConflictException($"A physical sensor alias already uses key: {definition.Key}");
                if (!byKey.TryAdd(definition.Key, definition))
                    throw new CalculationConflictException($"Duplicate calculated sensor key: {definition.Key}");
            }
            EnsureAcyclic(byKey);
            return [.. definitions.OrderBy(item => item.Key, StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is FormulaException or CalculationValidationException or CalculationConflictException)
        {
            throw new InvalidDataException($"Invalid calculated sensor configuration: {exception.Message}", exception);
        }
    }

    private FormulaType InferDefinition(
        string key,
        IReadOnlyDictionary<string, CalculatedSensorDefinition> definitions,
        HashSet<string> visiting)
    {
        if (!visiting.Add(key)) throw new FormulaException($"Calculated sensor cycle includes '{key}'");
        try
        {
            var expression = FormulaEngine.Parse(definitions[key].Formula);
            return FormulaEngine.Infer(expression, dependency =>
            {
                var alias = _aliases.GetDefinitions().FirstOrDefault(item => item.Key == dependency);
                if (alias is not null) return new FormulaType(alias.Quantity, alias.Unit);
                return definitions.ContainsKey(dependency)
                    ? InferDefinition(dependency, definitions, visiting)
                    : null;
            });
        }
        finally { visiting.Remove(key); }
    }

    private static void EnsureAcyclic(IReadOnlyDictionary<string, CalculatedSensorDefinition> definitions)
    {
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in definitions.Keys) Visit(key);
        return;

        void Visit(string key)
        {
            if (visited.Contains(key)) return;
            if (!visiting.Add(key)) throw new CalculationConflictException($"Calculated sensor cycle includes '{key}'.");
            foreach (var dependency in FormulaEngine.GetDependencies(FormulaEngine.Parse(definitions[key].Formula)))
                if (definitions.ContainsKey(dependency)) Visit(dependency);
            visiting.Remove(key);
            visited.Add(key);
        }
    }

    private static void ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128 || !KeyRegex().IsMatch(key))
            throw new CalculationValidationException("Calculated sensor key must be 1-128 lowercase characters, start with a letter, and use alphanumeric segments separated by '.' or '_'.");
    }

    private static string ValidateDisplayName(string displayName)
    {
        var value = displayName?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 256) throw new CalculationValidationException("Display name must be 1-256 characters.");
        return value;
    }

    private static string ValidateFormulaText(string formula)
    {
        var value = formula?.Trim() ?? string.Empty;
        if (value.Length is 0 or > 2048) throw new CalculationValidationException("Formula must be 1-2048 characters.");
        return value;
    }
}
