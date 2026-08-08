using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryLoom.Contracts.Aliases;
using TelemetryLoom.Contracts.Calculations;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;
using TelemetryLoom.Service.Pages;

namespace TelemetryLoom.Core.Tests;

public sealed class AliasPageModelTests
{
    [Fact]
    public void GetMapsExistingAliasTargetsAndConfigurationRevision()
    {
        var fixture = new AliasPageFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
        var model = fixture.CreateModel();

        var result = model.OnGet("temperature.input", null);

        Assert.IsType<PageResult>(result);
        Assert.Equal("temperature.input", model.CurrentAlias?.Key);
        Assert.Equal("fixture:temperature", model.Input.SensorId);
        Assert.Contains(model.TargetOptions, target => target.Id == "fixture:temperature");
        Assert.Equal(1, model.Configuration.Revision);
    }

    [Fact]
    public void SuccessfulSaveReturnsPostRedirectGetResultWithAuthoritativeFeedback()
    {
        var fixture = new AliasPageFixture();
        var model = fixture.CreateModel();
        model.Input = new AliasInput
        {
            Key = "temperature.input",
            DisplayName = "Input",
            SensorId = "fixture:temperature",
            Revision = 0
        };

        var result = Assert.IsType<RedirectToPageResult>(model.OnPostSave(null));

        Assert.Equal("/Aliases", result.PageName);
        Assert.Equal("temperature.input", result.RouteValues?["key"]);
        Assert.Contains("Configuration revision: 1", model.SuccessMessage, StringComparison.Ordinal);
        Assert.Equal("temperature.input", Assert.Single(fixture.Aliases.GetDefinitions()).Key);
    }

    [Fact]
    public void QuantityChangingRebindReturnsPageUntilServerConfirmation()
    {
        var fixture = new AliasPageFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
        fixture.Calculations.Upsert("temperature.double", "Double", "temperature.input * 2");
        var model = fixture.CreateModel();
        model.Input = new AliasInput
        {
            Key = "temperature.input",
            DisplayName = "Power Input",
            SensorId = "fixture:power",
            Revision = 2
        };

        var result = model.OnPostSave("temperature.input");

        Assert.IsType<PageResult>(result);
        Assert.True(model.RebindRequiresConfirmation);
        Assert.Equal("temperature.double", Assert.Single(model.DirectDependents).Key);
        Assert.False(model.ModelState.IsValid);
        Assert.Equal("fixture:temperature", Assert.Single(fixture.Aliases.GetDefinitions()).SensorId);
        Assert.Equal(2, fixture.Configuration.GetStatus().Revision);
    }

    private sealed class AliasPageFixture
    {
        private readonly SensorCatalog _sensors = new([new FixtureSource()]);

        public AliasPageFixture()
        {
            Configuration = new TelemetryConfigurationRegistry(new MemoryStore());
            Aliases = new SensorAliasRegistry(_sensors, Configuration);
            Calculations = new CalculatedSensorRegistry(Configuration, Aliases);
        }

        public TelemetryConfigurationRegistry Configuration { get; }
        public SensorAliasRegistry Aliases { get; }
        public CalculatedSensorRegistry Calculations { get; }

        public AliasesModel CreateModel() => new(Aliases, Calculations, _sensors, Configuration);
    }

    private sealed class FixtureSource : ISensorSource
    {
        private static readonly IReadOnlyList<SensorReading> Sensors =
        [
            Reading("fixture:temperature", "Temperature", QuantityKind.Temperature, UnitCode.Celsius, "°C"),
            Reading("fixture:power", "Power", QuantityKind.Power, UnitCode.Watts, "W")
        ];

        public string Name => "fixture";
        public IReadOnlyList<SensorReading> GetSensors() => Sensors;
        public SensorReading? GetSensor(string id) => Sensors.FirstOrDefault(sensor => sensor.Id == id);

        private static SensorReading Reading(
            string id,
            string name,
            QuantityKind quantity,
            UnitCode unit,
            string unitSymbol) =>
            new(
                id,
                name,
                null,
                1,
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
