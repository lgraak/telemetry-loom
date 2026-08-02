using System.Globalization;
using System.Text.RegularExpressions;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Units;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Collectors.Linux.Hwmon;

public sealed partial class HwmonSensorSource(
    IHwmonSnapshotSource snapshotSource,
    TimeProvider? timeProvider = null) : ISensorSource
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    [GeneratedRegex(@"^(temp|fan|in|curr|power|freq|humidity)(\d+)_(input|average)$")]
    private static partial Regex MeasurementRegex();

    private static readonly IReadOnlyDictionary<string, SensorTypeDefinition> SensorTypes =
        new Dictionary<string, SensorTypeDefinition>(StringComparer.Ordinal)
        {
            ["temp"] = new("Temperature", QuantityKind.Temperature, UnitCode.Celsius, 1_000d),
            ["fan"] = new("Fan", QuantityKind.RotationalSpeed, UnitCode.RevolutionsPerMinute, 1d),
            ["in"] = new("Voltage", QuantityKind.Voltage, UnitCode.Volts, 1_000d),
            ["curr"] = new("Current", QuantityKind.Current, UnitCode.Amperes, 1_000d),
            ["power"] = new("Power", QuantityKind.Power, UnitCode.Watts, 1_000_000d),
            ["freq"] = new("Frequency", QuantityKind.Frequency, UnitCode.Hertz, 1d),
            ["humidity"] = new("Humidity", QuantityKind.Percentage, UnitCode.Percent, 1_000d)
        };

    public string Name => "hwmon";

    public IReadOnlyList<SensorReading> GetSensors()
    {
        var sensors = new List<SensorReading>();

        foreach (var device in snapshotSource.Capture().Devices)
        {
            foreach (var attribute in device.Attributes.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                var match = MeasurementRegex().Match(attribute.Key);
                if (!match.Success || !SensorTypes.TryGetValue(match.Groups[1].Value, out var definition))
                {
                    continue;
                }

                var sensorType = match.Groups[1].Value;
                var channel = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                var measurement = match.Groups[3].Value;
                sensors.Add(CreateReading(device, attribute.Key, attribute.Value, sensorType, channel, measurement, definition));
            }
        }

        return sensors;
    }

    public SensorReading? GetSensor(string id) =>
        GetSensors().FirstOrDefault(sensor => string.Equals(sensor.Id, id, StringComparison.Ordinal));

    private SensorReading CreateReading(
        HwmonDeviceSnapshot device,
        string attributeName,
        string rawValue,
        string sensorType,
        int channel,
        string measurement,
        SensorTypeDefinition definition)
    {
        var unit = UnitCatalog.Get(definition.Unit);
        var labelKey = $"{sensorType}{channel}_label";
        var faultKey = $"{sensorType}{channel}_fault";
        var hasFault = device.Attributes.TryGetValue(faultKey, out var fault) && fault == "1";
        var parsed = double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var rawNumber);
        var available = parsed && !hasFault;
        var label = device.Attributes.TryGetValue(labelKey, out var configuredLabel) && !string.IsNullOrWhiteSpace(configuredLabel)
            ? configuredLabel
            : $"{device.DriverName} {definition.DisplayName} {channel}";

        return new SensorReading(
            HwmonStableId.Create(device.DriverName, device.HardwarePath, sensorType, channel, measurement),
            label,
            null,
            available ? rawNumber / definition.Divisor : null,
            definition.Quantity,
            unit.Code,
            unit.Symbol,
            Name,
            available ? SensorStatus.Available : SensorStatus.Unavailable,
            available ? _timeProvider.GetUtcNow() : null,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["driver"] = device.DriverName,
                ["hardwarePath"] = HwmonPathIdentity.NormalizeHardwarePath(device.HardwarePath),
                ["identityQuality"] = device.IdentityQuality.ToString(),
                ["sourceClassName"] = device.ClassName,
                ["sensorType"] = sensorType,
                ["channel"] = channel.ToString(CultureInfo.InvariantCulture),
                ["measurement"] = measurement,
                ["inputAttribute"] = attributeName,
                ["nativeScaleDivisor"] = definition.Divisor.ToString(CultureInfo.InvariantCulture)
            });
    }

    private sealed record SensorTypeDefinition(
        string DisplayName,
        QuantityKind Quantity,
        UnitCode Unit,
        double Divisor);
}
