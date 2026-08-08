using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;
using TelemetryLoom.Service.Pages;

namespace TelemetryLoom.Core.Tests;

public sealed class CalculationPageModelTests
{
    [Fact]
    public void GetMapsCalculationListReadingAndOperandStatuses()
    {
        var fixture = new CalculationPageFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
        fixture.Aliases.Upsert("temperature.stale", "Stale Input", "fixture:stale");
        fixture.Calculations.Upsert("temperature.scaled", "Scaled", "temperature.input * 2");
        var model = fixture.CreateModel();

        var result = model.OnGet("temperature.scaled");

        Assert.IsType<PageResult>(result);
        Assert.Equal("temperature.scaled", model.CurrentDefinition?.Key);
        Assert.Equal(SensorStatus.Available, model.CurrentReading?.Status);
        Assert.Equal("temperature.scaled", Assert.Single(model.CalculationList).Definition.Key);
        Assert.Contains(model.Operands, operand =>
            operand.Key == "temperature.stale" && operand.Status == SensorStatus.Stale);
        Assert.Contains(model.Operands, operand =>
            operand.Key == "temperature.scaled" && operand.Origin == "Calculated");
    }

    [Fact]
    public void ValidateReturnsTypedDependenciesWithoutWriting()
    {
        var fixture = new CalculationPageFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
        var revisionBefore = fixture.Configuration.GetStatus().Revision;
        var model = fixture.CreateModel();
        model.Input = new CalculationInput
        {
            Key = "temperature.scaled",
            DisplayName = "Scaled",
            Formula = "temperature.input * 2",
            Revision = revisionBefore
        };

        var result = model.OnPostValidate(null);

        Assert.IsType<PageResult>(result);
        Assert.Equal(QuantityKind.Temperature, model.Validation?.Quantity);
        Assert.Equal(UnitCode.Celsius, model.Validation?.Unit);
        Assert.Equal("temperature.input", Assert.Single(model.Validation!.Dependencies).Key);
        Assert.Empty(fixture.Calculations.GetDefinitions());
        Assert.Equal(revisionBefore, fixture.Configuration.GetStatus().Revision);
    }

    [Fact]
    public void SuccessfulSaveReturnsPostRedirectGetAndAuthoritativeStatus()
    {
        var fixture = new CalculationPageFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
        var model = fixture.CreateModel();
        model.Input = new CalculationInput
        {
            Key = "temperature.scaled",
            DisplayName = "Scaled",
            Formula = "temperature.input * 2",
            Revision = 1
        };

        var result = Assert.IsType<RedirectToPageResult>(model.OnPostSave(null));

        Assert.Equal("/Calculations", result.PageName);
        Assert.Equal("temperature.scaled", result.RouteValues?["key"]);
        Assert.Contains("Status: Available", model.SuccessMessage, StringComparison.Ordinal);
        Assert.Equal(2, fixture.Configuration.GetStatus().Revision);
    }

    [Fact]
    public void EditRejectsKeyChangeWithoutWriting()
    {
        var fixture = new CalculationPageFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
        fixture.Calculations.Upsert("temperature.scaled", "Scaled", "temperature.input * 2");
        var revisionBefore = fixture.Configuration.GetStatus().Revision;
        var model = fixture.CreateModel();
        model.Input = new CalculationInput
        {
            Key = "temperature.renamed",
            DisplayName = "Renamed",
            Formula = "temperature.input * 3",
            Revision = revisionBefore
        };

        var result = model.OnPostSave("temperature.scaled");

        Assert.IsType<PageResult>(result);
        Assert.False(model.ModelState.IsValid);
        var definition = Assert.Single(fixture.Calculations.GetDefinitions());
        Assert.Equal("temperature.scaled", definition.Key);
        Assert.Equal("temperature.input * 2", definition.Formula);
        Assert.Equal(revisionBefore, fixture.Configuration.GetStatus().Revision);
    }

    [Fact]
    public void DeleteRequiresConfirmationAndListsDirectDependents()
    {
        var fixture = new CalculationPageFixture();
        fixture.Aliases.Upsert("temperature.input", "Input", "fixture:temperature");
        fixture.Calculations.Upsert("temperature.base", "Base", "temperature.input * 2");
        fixture.Calculations.Upsert("temperature.dependent", "Dependent", "temperature.base * 2");
        var model = fixture.CreateModel();
        model.Input.Revision = fixture.Configuration.GetStatus().Revision;

        var result = model.OnPostDelete("temperature.base");

        Assert.IsType<PageResult>(result);
        Assert.Equal("temperature.dependent", Assert.Single(model.DirectDependents).Key);
        Assert.NotNull(fixture.Calculations.GetDefinition("temperature.base"));
        Assert.False(model.ModelState.IsValid);
    }

    private sealed class CalculationPageFixture
    {
        private readonly SensorCatalog _sensors = new([new FixtureSource()]);
        private readonly AliasedSensorCatalog _aliased;
        private readonly CalculatedSensorCatalog _catalog;

        public CalculationPageFixture()
        {
            Configuration = new TelemetryConfigurationRegistry(new MemoryStore());
            Aliases = new SensorAliasRegistry(_sensors, Configuration);
            _aliased = new AliasedSensorCatalog(_sensors, Aliases);
            Calculations = new CalculatedSensorRegistry(Configuration, Aliases);
            _catalog = new CalculatedSensorCatalog(_aliased, Calculations);
        }

        public TelemetryConfigurationRegistry Configuration { get; }
        public SensorAliasRegistry Aliases { get; }
        public CalculatedSensorRegistry Calculations { get; }

        public CalculationsModel CreateModel() =>
            new(Calculations, _catalog, Aliases, Configuration);
    }

    private sealed class FixtureSource : ISensorSource
    {
        private static readonly IReadOnlyList<SensorReading> Readings =
        [
            Reading("fixture:temperature", "Temperature", SensorStatus.Available),
            Reading("fixture:stale", "Stale Temperature", SensorStatus.Stale)
        ];

        public string Name => "fixture";
        public IReadOnlyList<SensorReading> GetSensors() => Readings;
        public SensorReading? GetSensor(string id) => Readings.FirstOrDefault(reading => reading.Id == id);

        private static SensorReading Reading(string id, string name, SensorStatus status) =>
            new(
                id,
                name,
                null,
                20,
                QuantityKind.Temperature,
                UnitCode.Celsius,
                "°C",
                "fixture",
                status,
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
