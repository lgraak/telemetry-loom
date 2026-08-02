using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Contracts.Units;

public static class UnitCatalog
{
    private static readonly IReadOnlyDictionary<UnitCode, UnitDefinition> Definitions =
        new Dictionary<UnitCode, UnitDefinition>
        {
            [UnitCode.Unknown] = new(UnitCode.Unknown, QuantityKind.Unknown, string.Empty),
            [UnitCode.None] = new(UnitCode.None, QuantityKind.Dimensionless, string.Empty),
            [UnitCode.Celsius] = new(UnitCode.Celsius, QuantityKind.Temperature, "°C"),
            [UnitCode.DeltaCelsius] = new(UnitCode.DeltaCelsius, QuantityKind.TemperatureDelta, "Δ°C"),
            [UnitCode.Fahrenheit] = new(UnitCode.Fahrenheit, QuantityKind.Temperature, "°F"),
            [UnitCode.DeltaFahrenheit] = new(UnitCode.DeltaFahrenheit, QuantityKind.TemperatureDelta, "Δ°F"),
            [UnitCode.MetersPerSecond] = new(UnitCode.MetersPerSecond, QuantityKind.Speed, "m/s"),
            [UnitCode.MilesPerHour] = new(UnitCode.MilesPerHour, QuantityKind.Speed, "mph"),
            [UnitCode.Watts] = new(UnitCode.Watts, QuantityKind.Power, "W"),
            [UnitCode.RevolutionsPerMinute] = new(UnitCode.RevolutionsPerMinute, QuantityKind.RotationalSpeed, "RPM"),
            [UnitCode.Percent] = new(UnitCode.Percent, QuantityKind.Percentage, "%"),
            [UnitCode.Volts] = new(UnitCode.Volts, QuantityKind.Voltage, "V"),
            [UnitCode.Amperes] = new(UnitCode.Amperes, QuantityKind.Current, "A"),
            [UnitCode.Hertz] = new(UnitCode.Hertz, QuantityKind.Frequency, "Hz"),
            [UnitCode.Megahertz] = new(UnitCode.Megahertz, QuantityKind.Frequency, "MHz"),
            [UnitCode.Megabytes] = new(UnitCode.Megabytes, QuantityKind.DataSize, "MB")
        };

    public static UnitDefinition Get(UnitCode code) => Definitions[code];

    public static bool AreCompatible(UnitCode left, UnitCode right) =>
        Get(left).Quantity == Get(right).Quantity;
}
