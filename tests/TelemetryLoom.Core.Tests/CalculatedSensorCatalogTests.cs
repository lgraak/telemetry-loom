using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class CalculatedSensorCatalogTests
{
    [Fact]
    public void EvaluatesTemperatureDeltaAndCalculatedDependencyChain()
    {
        var fixture = CreateFixture(
            Reading("fixture:in", 20, SensorStatus.Available, "2026-08-02T00:00:02Z"),
            Reading("fixture:out", 40, SensorStatus.Available, "2026-08-02T00:00:01Z"));
        fixture.Aliases.Upsert("air.in", "Air In", "fixture:in");
        fixture.Aliases.Upsert("air.out", "Air Out", "fixture:out");
        fixture.Calculations.Upsert("air.delta", "Air Delta", "air.out - air.in");
        fixture.Calculations.Upsert("air.double_delta", "Double Delta", "air.delta * 2");

        var delta = fixture.Catalog.GetSensorByAlias("air.delta")!;
        var doubled = fixture.Catalog.GetSensorByAlias("air.double_delta")!;

        Assert.Equal(20, delta.Value);
        Assert.Equal(UnitCode.DeltaCelsius, delta.Unit);
        Assert.Equal(DateTimeOffset.Parse("2026-08-02T00:00:01Z"), delta.LastSuccessfulUpdate);
        Assert.Equal(40, doubled.Value);
        Assert.Equal(SensorStatus.Available, doubled.Status);
    }

    [Theory]
    [InlineData(SensorStatus.CalculationError, SensorStatus.MissingDependency, SensorStatus.CalculationError)]
    [InlineData(SensorStatus.MissingDependency, SensorStatus.Unavailable, SensorStatus.MissingDependency)]
    [InlineData(SensorStatus.Unavailable, SensorStatus.Stale, SensorStatus.Unavailable)]
    [InlineData(SensorStatus.Stale, SensorStatus.Available, SensorStatus.Stale)]
    public void AppliesDependencyStatusPrecedence(SensorStatus first, SensorStatus second, SensorStatus expected)
    {
        var dependencies = new[]
        {
            Reading("fixture:first", first is SensorStatus.Available or SensorStatus.Stale ? 10 : null, first, "2026-08-02T00:00:00Z"),
            Reading("fixture:second", second is SensorStatus.Available or SensorStatus.Stale ? 5 : null, second, "2026-08-02T00:00:00Z")
        };
        var fixture = CreateFixture(dependencies);
        fixture.Aliases.Upsert("value.first", "First", "fixture:first");
        fixture.Aliases.Upsert("value.second", "Second", "fixture:second");
        fixture.Calculations.Upsert("value.result", "Result", "value.first - value.second");

        var result = fixture.Catalog.GetSensorByAlias("value.result")!;

        Assert.Equal(expected, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public void DeletedAliasBecomesMissingDependencyAndCycleIsRejected()
    {
        var fixture = CreateFixture(Reading("fixture:value", 10, SensorStatus.Available, "2026-08-02T00:00:00Z"));
        fixture.Aliases.Upsert("value.input", "Input", "fixture:value");
        fixture.Calculations.Upsert("value.one", "One", "value.input * 2");
        fixture.Calculations.Upsert("value.two", "Two", "value.one * 2");

        Assert.Throws<CalculationConflictException>(() =>
            fixture.Calculations.Upsert("value.one", "One", "value.two * 2"));
        fixture.Aliases.Delete("value.input");

        Assert.Equal(SensorStatus.MissingDependency, fixture.Catalog.GetSensorByAlias("value.one")!.Status);
    }

    private static Fixture CreateFixture(params SensorReading[] readings)
    {
        var sensors = new SensorCatalog([new FixtureSource(readings)]);
        var configuration = new TelemetryConfigurationRegistry(new MemoryConfigurationStore());
        var aliases = new SensorAliasRegistry(sensors, configuration);
        var physical = new AliasedSensorCatalog(sensors, aliases);
        var calculations = new CalculatedSensorRegistry(configuration, aliases);
        return new Fixture(aliases, calculations, new CalculatedSensorCatalog(physical, calculations));
    }

    private static SensorReading Reading(string id, double? value, SensorStatus status, string timestamp) =>
        new(id, id, null, value, QuantityKind.Temperature, UnitCode.Celsius, "°C", "fixture", status,
            DateTimeOffset.Parse(timestamp), new Dictionary<string, string>());

    private sealed record Fixture(
        SensorAliasRegistry Aliases,
        CalculatedSensorRegistry Calculations,
        CalculatedSensorCatalog Catalog);

    private sealed class FixtureSource(IReadOnlyList<SensorReading> readings) : ISensorSource
    {
        public string Name => "fixture";
        public IReadOnlyList<SensorReading> GetSensors() => readings;
        public SensorReading? GetSensor(string id) => readings.FirstOrDefault(item => item.Id == id);
    }

    private sealed class MemoryConfigurationStore : ITelemetryConfigurationStore
    {
        private TelemetryConfigurationDocument _document = new();
        public TelemetryConfigurationDocument Load() => _document;
        public void Save(TelemetryConfigurationDocument document) => _document = document;
    }
}
