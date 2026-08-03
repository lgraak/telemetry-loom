using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Calculations;

namespace TelemetryLoom.Core.Tests;

public sealed class FormulaEngineTests
{
    [Fact]
    public void ParserHonorsPrecedenceParenthesesAndUnaryNegative()
    {
        Assert.Equal(7, Evaluate("1 + 2 * 3"));
        Assert.Equal(9, Evaluate("(1 + 2) * 3"));
        Assert.Equal(-6, Evaluate("-(1 + 2) * 2"));
    }

    [Fact]
    public void DependenciesAreUniqueAndPreserveFirstAppearance()
    {
        var dependencies = FormulaEngine.GetDependencies(
            FormulaEngine.Parse("cooling.out - cooling.in + cooling.out"));

        Assert.Equal(["cooling.out", "cooling.in"], dependencies);
    }

    [Theory]
    [InlineData("1.")]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("Sensor.bad")]
    public void ParserRejectsMalformedOrUnsupportedSyntax(string formula) =>
        Assert.Throws<FormulaException>(() => FormulaEngine.Parse(formula));

    [Fact]
    public void TemperatureSubtractionProducesDeltaAndDeltaCanBeAddedBack()
    {
        var types = new Dictionary<string, FormulaType>
        {
            ["air.out"] = new(QuantityKind.Temperature, UnitCode.Celsius),
            ["air.in"] = new(QuantityKind.Temperature, UnitCode.Celsius),
            ["air.delta"] = new(QuantityKind.TemperatureDelta, UnitCode.DeltaCelsius)
        };

        Assert.Equal(
            new FormulaType(QuantityKind.TemperatureDelta, UnitCode.DeltaCelsius),
            FormulaEngine.Infer(FormulaEngine.Parse("air.out - air.in"), key => types[key]));
        Assert.Equal(
            new FormulaType(QuantityKind.Temperature, UnitCode.Celsius),
            FormulaEngine.Infer(FormulaEngine.Parse("air.in + air.delta"), key => types[key]));
    }

    [Theory]
    [InlineData("absolute + absolute")]
    [InlineData("delta - absolute")]
    [InlineData("absolute + scalar")]
    [InlineData("absolute * delta")]
    [InlineData("scalar / absolute")]
    public void RejectsUnsupportedDimensionalOperations(string formula)
    {
        var types = new Dictionary<string, FormulaType>
        {
            ["absolute"] = new(QuantityKind.Temperature, UnitCode.Celsius),
            ["delta"] = new(QuantityKind.TemperatureDelta, UnitCode.DeltaCelsius),
            ["scalar"] = new(QuantityKind.Dimensionless, UnitCode.None)
        };
        Assert.Throws<FormulaException>(() => FormulaEngine.Infer(FormulaEngine.Parse(formula), key => types[key]));
    }

    [Fact]
    public void RequiresExactUnitsAndRejectsDivisionByZero()
    {
        var types = new Dictionary<string, FormulaType>
        {
            ["metric"] = new(QuantityKind.Speed, UnitCode.MetersPerSecond),
            ["imperial"] = new(QuantityKind.Speed, UnitCode.MilesPerHour)
        };
        Assert.Throws<FormulaException>(() =>
            FormulaEngine.Infer(FormulaEngine.Parse("metric + imperial"), key => types[key]));
        Assert.Throws<FormulaException>(() => FormulaEngine.Evaluate(FormulaEngine.Parse("10 / 0"), _ => default));
    }

    private static double Evaluate(string formula) => FormulaEngine.Evaluate(
        FormulaEngine.Parse(formula),
        _ => throw new InvalidOperationException()).Value;
}
