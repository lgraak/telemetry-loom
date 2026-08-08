using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class CalculatedSensorDefinitionValidatorTests
{
    [Fact]
    public void ValidationReturnsNormalizedDefinitionOrderedDependenciesAndInferredType()
    {
        var fixture = new ValidatorFixture();
        fixture.Aliases.Upsert("temperature.in", "Input", "fixture:temperature:in");
        fixture.Aliases.Upsert("temperature.out", "Output", "fixture:temperature:out");

        var result = fixture.Registry.Validate(
            "temperature.delta",
            " Temperature Delta ",
            " temperature.out - temperature.in + temperature.out - temperature.out ");

        Assert.Equal("Temperature Delta", result.Definition.DisplayName);
        Assert.Equal("temperature.out - temperature.in + temperature.out - temperature.out", result.Definition.Formula);
        Assert.Equal(QuantityKind.TemperatureDelta, result.Definition.Quantity);
        Assert.Equal(UnitCode.DeltaCelsius, result.Definition.Unit);
        Assert.Equal(["temperature.out", "temperature.in"], result.Dependencies);
    }

    [Fact]
    public void ValidateAndSaveUseIdenticalSemanticResult()
    {
        var fixture = new ValidatorFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature:in");

        var validation = fixture.Registry.Validate(
            "temperature.scaled", "Scaled", "temperature.input * 2");
        var saved = fixture.Registry.Upsert(
            "temperature.scaled", "Scaled", "temperature.input * 2");

        Assert.Equal(validation.Definition, saved);
    }

    [Fact]
    public void ValidationReportsParsePositionAndUnknownOperands()
    {
        var fixture = new ValidatorFixture();

        var parse = Assert.Throws<CalculationValidationException>(() =>
            fixture.Registry.Validate("value.parse", "Parse", "1 +"));
        var unknown = Assert.Throws<CalculationValidationException>(() =>
            fixture.Registry.Validate("value.unknown", "Unknown", "missing.operand * 2"));

        Assert.Contains("position", parse.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unknown sensor alias 'missing.operand'", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationRejectsDimensionalIncompatibility()
    {
        var fixture = new ValidatorFixture();
        fixture.Aliases.Upsert("temperature.input", "Temperature", "fixture:temperature:in");
        fixture.Aliases.Upsert("power.input", "Power", "fixture:power");

        var exception = Assert.Throws<CalculationValidationException>(() =>
            fixture.Registry.Validate(
                "value.invalid", "Invalid", "temperature.input + power.input"));

        Assert.Contains("matching absolute and delta unit families", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationRejectsCyclesAndSharedNamespaceCollisions()
    {
        var fixture = new ValidatorFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature:in");
        fixture.Registry.Upsert("temperature.one", "One", "temperature.input * 2");
        fixture.Registry.Upsert("temperature.two", "Two", "temperature.one * 2");

        var cycle = Assert.Throws<CalculationConflictException>(() =>
            fixture.Registry.Validate("temperature.one", "One", "temperature.two * 2"));
        var collision = Assert.Throws<CalculationConflictException>(() =>
            fixture.Registry.Validate("temperature.input", "Collision", "1"));

        Assert.Contains("cycle", cycle.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("physical sensor alias", collision.Message, StringComparison.Ordinal);
    }

    private sealed class ValidatorFixture
    {
        private readonly SensorCatalog _sensors = new([new FixtureSource()]);

        public ValidatorFixture()
        {
            var configuration = new TelemetryConfigurationRegistry(new MemoryStore());
            Aliases = new SensorAliasRegistry(_sensors, configuration);
            Registry = new CalculatedSensorRegistry(configuration, Aliases);
        }

        public SensorAliasRegistry Aliases { get; }
        public CalculatedSensorRegistry Registry { get; }
    }

    private sealed class FixtureSource : ISensorSource
    {
        private static readonly IReadOnlyList<SensorReading> Readings =
        [
            Reading("fixture:temperature:in", QuantityKind.Temperature, UnitCode.Celsius, "°C"),
            Reading("fixture:temperature:out", QuantityKind.Temperature, UnitCode.Celsius, "°C"),
            Reading("fixture:power", QuantityKind.Power, UnitCode.Watts, "W")
        ];

        public string Name => "fixture";
        public IReadOnlyList<SensorReading> GetSensors() => Readings;
        public SensorReading? GetSensor(string id) => Readings.FirstOrDefault(reading => reading.Id == id);

        private static SensorReading Reading(
            string id,
            QuantityKind quantity,
            UnitCode unit,
            string unitSymbol) =>
            new(
                id,
                id,
                null,
                10,
                quantity,
                unit,
                unitSymbol,
                "fixture",
                SensorStatus.Available,
                DateTimeOffset.Parse("2026-08-07T12:00:00Z"),
                new Dictionary<string, string>());
    }

    private sealed class MemoryStore : ITelemetryConfigurationStore
    {
        private TelemetryConfigurationDocument _document = new();
        public TelemetryConfigurationDocument Load() => _document;
        public void Save(TelemetryConfigurationDocument document) => _document = document;
    }
}
