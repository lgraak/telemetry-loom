using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Configuration;

namespace TelemetryLoom.Core.Calculations;

public sealed class CalculatedSensorRegistry
{
    private readonly object _gate = new();
    private readonly TelemetryConfigurationRegistry _configuration;
    private readonly CalculatedSensorDefinitionValidator _validator;
    private IReadOnlyList<CalculatedSensorDefinition> _definitions;

    public CalculatedSensorRegistry(TelemetryConfigurationRegistry configuration, SensorAliasRegistry aliases)
    {
        _configuration = configuration;
        _validator = new CalculatedSensorDefinitionValidator(aliases);
        _definitions = _validator.ValidateLoaded(configuration.GetSnapshot().CalculatedSensors);
    }

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

    public CalculatedDefinitionValidationResult Validate(
        string key,
        string displayName,
        string formula)
    {
        lock (_gate)
        {
            return _validator.Validate(key, displayName, formula, _definitions);
        }
    }

    public CalculatedSensorDefinition Upsert(string key, string displayName, string formula)
    {
        return UpsertCore(key, displayName, formula, null);
    }

    public CalculatedSensorDefinition Upsert(
        string key,
        string displayName,
        string formula,
        long expectedRevision)
    {
        _configuration.EnsureRevision(expectedRevision);
        return UpsertCore(key, displayName, formula, expectedRevision);
    }

    private CalculatedSensorDefinition UpsertCore(
        string key,
        string displayName,
        string formula,
        long? expectedRevision)
    {
        lock (_gate)
        {
            var validation = _validator.Validate(key, displayName, formula, _definitions);
            var definition = validation.Definition;
            var next = _definitions
                .Where(item => !string.Equals(item.Key, key, StringComparison.Ordinal))
                .Append(definition)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ToArray();
            if (expectedRevision is { } revision)
            {
                _configuration.UpdateCalculatedSensors(next, revision);
            }
            else
            {
                _configuration.UpdateCalculatedSensors(next);
            }

            _definitions = next;
            return definition;
        }
    }

    public bool Delete(string key)
    {
        return DeleteCore(key, null);
    }

    public bool Delete(string key, long expectedRevision)
    {
        _configuration.EnsureRevision(expectedRevision);
        return DeleteCore(key, expectedRevision);
    }

    private bool DeleteCore(string key, long? expectedRevision)
    {
        lock (_gate)
        {
            var next = _definitions.Where(item => !string.Equals(item.Key, key, StringComparison.Ordinal)).ToArray();
            if (next.Length == _definitions.Count)
            {
                return false;
            }

            if (expectedRevision is { } revision)
            {
                _configuration.UpdateCalculatedSensors(next, revision);
            }
            else
            {
                _configuration.UpdateCalculatedSensors(next);
            }

            _definitions = next;
            return true;
        }
    }
}
