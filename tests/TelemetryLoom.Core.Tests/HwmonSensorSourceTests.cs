using TelemetryLoom.Collectors.Linux.Hwmon;
using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class HwmonSensorSourceTests
{
    [Fact]
    public void HwmonRenumberingDoesNotChangeSensorIds()
    {
        var before = LoadSource("hwmon3.json").GetSensors();
        var after = LoadSource("hwmon9.json").GetSensors();

        Assert.Equal(before.Select(sensor => sensor.Id), after.Select(sensor => sensor.Id));
        Assert.All(before, sensor => Assert.DoesNotContain("hwmon3", sensor.Id, StringComparison.Ordinal));
        Assert.All(after, sensor => Assert.DoesNotContain("hwmon9", sensor.Id, StringComparison.Ordinal));
    }

    [Fact]
    public void NormalizesSupportedHwmonQuantities()
    {
        var sensors = LoadSource("hwmon3.json").GetSensors();

        Assert.Equal(9, sensors.Count);
        AssertReading(sensors, "temp", "input", 1, 30d, QuantityKind.Temperature, UnitCode.Celsius);
        AssertReading(sensors, "fan", "input", 1, 1250d, QuantityKind.RotationalSpeed, UnitCode.RevolutionsPerMinute);
        AssertReading(sensors, "in", "input", 0, 1.2d, QuantityKind.Voltage, UnitCode.Volts);
        AssertReading(sensors, "curr", "input", 1, 2.5d, QuantityKind.Current, UnitCode.Amperes);
        AssertReading(sensors, "power", "input", 1, 150d, QuantityKind.Power, UnitCode.Watts);
        AssertReading(sensors, "power", "average", 1, 145d, QuantityKind.Power, UnitCode.Watts);
        AssertReading(sensors, "freq", "input", 1, 1_500_000_000d, QuantityKind.Frequency, UnitCode.Hertz);
        AssertReading(sensors, "humidity", "input", 1, 45.5d, QuantityKind.Percentage, UnitCode.Percent);
    }

    [Fact]
    public void FaultedSensorIsUnavailableInsteadOfZero()
    {
        var sensor = LoadSource("hwmon3.json").GetSensors().Single(reading =>
            reading.Metadata["sensorType"] == "temp" && reading.Metadata["channel"] == "2");

        Assert.Equal(SensorStatus.Unavailable, sensor.Status);
        Assert.Null(sensor.Value);
        Assert.Null(sensor.LastSuccessfulUpdate);
    }

    [Fact]
    public void LabelIsDisplayMetadataAndDoesNotAffectStableId()
    {
        var fixture = LoadFixture("hwmon3.json");
        var original = new HwmonSensorSource(new FixtureHwmonSnapshotSource(fixture)).GetSensors()
            .Single(sensor => sensor.Metadata["sensorType"] == "temp" && sensor.Metadata["channel"] == "1");
        fixture.Devices[0].Attributes["temp1_label"] = "Renamed Intake";

        var renamed = new HwmonSensorSource(new FixtureHwmonSnapshotSource(fixture)).GetSensors()
            .Single(sensor => sensor.Metadata["sensorType"] == "temp" && sensor.Metadata["channel"] == "1");

        Assert.Equal(original.Id, renamed.Id);
        Assert.NotEqual(original.DisplayName, renamed.DisplayName);
    }

    private static HwmonSensorSource LoadSource(string name) =>
        new(FixtureHwmonSnapshotSource.FromFile(FixturePath(name)));

    private static HwmonFixture LoadFixture(string name) =>
        HwmonFixtureSerializer.Load(FixturePath(name));

    private static string FixturePath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", name);

    private static void AssertReading(
        IReadOnlyList<SensorReading> sensors,
        string sensorType,
        string measurement,
        int channel,
        double expectedValue,
        QuantityKind expectedQuantity,
        UnitCode expectedUnit)
    {
        var sensor = sensors.Single(reading =>
            reading.Metadata["sensorType"] == sensorType &&
            reading.Metadata["measurement"] == measurement &&
            reading.Metadata["channel"] == channel.ToString());

        Assert.Equal(expectedValue, sensor.Value);
        Assert.Equal(expectedQuantity, sensor.Quantity);
        Assert.Equal(expectedUnit, sensor.Unit);
        Assert.Equal(SensorStatus.Available, sensor.Status);
    }
}
