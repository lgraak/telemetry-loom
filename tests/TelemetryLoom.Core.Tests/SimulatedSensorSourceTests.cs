using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class SimulatedSensorSourceTests
{
    [Fact]
    public void PublishesStableStructuredTemperatureSensor()
    {
        var source = new SimulatedSensorSource();

        var sensor = Assert.Single(source.GetSensors());

        Assert.Equal(SimulatedSensorSource.TemperatureSensorId, sensor.Id);
        Assert.Equal("demo.temperature", sensor.Alias);
        Assert.Equal(30.0, sensor.Value);
        Assert.Equal(QuantityKind.Temperature, sensor.Quantity);
        Assert.Equal(UnitCode.Celsius, sensor.Unit);
        Assert.Equal("°C", sensor.UnitSymbol);
        Assert.Equal(SensorStatus.Available, sensor.Status);
        Assert.NotNull(sensor.LastSuccessfulUpdate);
    }

    [Fact]
    public void UnknownSensorReturnsNull()
    {
        var source = new SimulatedSensorSource();

        Assert.Null(source.GetSensor("simulated:missing"));
    }
}
