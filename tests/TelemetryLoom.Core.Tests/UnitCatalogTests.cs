using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Units;

namespace TelemetryLoom.Core.Tests;

public sealed class UnitCatalogTests
{
    [Fact]
    public void TemperatureUnitsAreCompatible()
    {
        Assert.True(UnitCatalog.AreCompatible(UnitCode.Celsius, UnitCode.Fahrenheit));
    }

    [Fact]
    public void TemperatureAndPowerAreNotCompatible()
    {
        Assert.False(UnitCatalog.AreCompatible(UnitCode.Celsius, UnitCode.Watts));
    }

    [Fact]
    public void TemperatureDeltaIsDistinctFromAbsoluteTemperature()
    {
        Assert.False(UnitCatalog.AreCompatible(UnitCode.Celsius, UnitCode.DeltaCelsius));
    }
}
