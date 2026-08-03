using System.Text.RegularExpressions;
using TelemetryLoom.Contracts.Sensors;

namespace TelemetryLoom.Core.Presentation;

public sealed partial class SensorPresentationRegistry
{
    private static readonly IReadOnlyList<PresentationRule> Rules =
    [
        Rule("k10temp", "Tctl", QuantityKind.Temperature,
            "CPU Control Temperature",
            "Temperature used by AMD platform cooling and thermal control logic.",
            "CPU", "AMD CPU", "Temperature", InterpretationConfidence.Known, "k10temp.tctl"),
        Rule("k10temp", "Tdie", QuantityKind.Temperature,
            "CPU Die Temperature",
            "AMD CPU die temperature reported by the k10temp driver.",
            "CPU", "AMD CPU", "Temperature", InterpretationConfidence.Known, "k10temp.tdie"),
        Rule("amdgpu", "edge", QuantityKind.Temperature,
            "GPU Edge Temperature",
            "Temperature reported for the edge sensor by the AMD GPU driver.",
            "GPU", "AMD GPU", "Temperature", InterpretationConfidence.Known, "amdgpu.edge"),
        Rule("amdgpu", "junction", QuantityKind.Temperature,
            "GPU Junction Temperature",
            "Hotspot or junction temperature reported by the AMD GPU driver.",
            "GPU", "AMD GPU", "Temperature", InterpretationConfidence.Known, "amdgpu.junction"),
        Rule("amdgpu", "mem", QuantityKind.Temperature,
            "GPU Memory Temperature",
            "Memory temperature reported by the AMD GPU driver.",
            "GPU", "AMD GPU", "Temperature", InterpretationConfidence.Known, "amdgpu.mem"),
        Rule("amdgpu", "sclk", QuantityKind.Frequency,
            "GPU Core Clock",
            "Current graphics or compute engine clock reported by the AMD GPU driver.",
            "GPU", "AMD GPU", "Frequency", InterpretationConfidence.Known, "amdgpu.sclk"),
        Rule("amdgpu", "mclk", QuantityKind.Frequency,
            "GPU Memory Clock",
            "Current memory clock reported by the AMD GPU driver.",
            "GPU", "AMD GPU", "Frequency", InterpretationConfidence.Known, "amdgpu.mclk"),
        Rule("amdgpu", "PPT", QuantityKind.Power,
            "Average GPU Package Power",
            "Average power reading reported by the AMD GPU driver.",
            "GPU", "AMD GPU", "Power", InterpretationConfidence.Known, "amdgpu.ppt", "average"),
        Rule("amdgpu", "PPT", QuantityKind.Power,
            "GPU Package Power",
            "Current power reading reported by the AMD GPU driver.",
            "GPU", "AMD GPU", "Power", InterpretationConfidence.Known, "amdgpu.ppt", "input"),
        Rule("nvme", "Composite", QuantityKind.Temperature,
            "NVMe Composite Temperature",
            "Overall thermal value selected by the NVMe device.",
            "Storage", "NVMe", "Temperature", InterpretationConfidence.Known, "nvme.composite")
    ];

    [GeneratedRegex(@"^Tccd(?<number>[1-9][0-9]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex K10TempCcdRegex();

    [GeneratedRegex(@"^Sensor (?<number>[1-9][0-9]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex NvmeSensorRegex();

    [GeneratedRegex(@"^Package id (?<number>[0-9]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CoreTempPackageRegex();

    [GeneratedRegex(@"^Core (?<number>[0-9]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex CoreTempCoreRegex();

    public SensorReading Enrich(SensorReading reading)
    {
        var context = PresentationContext.From(reading);
        var presentation = FindKnown(context) ?? CreateGeneric(context);
        return reading with
        {
            DisplayName = presentation.DisplayName,
            Presentation = presentation
        };
    }

    private static SensorPresentation? FindKnown(PresentationContext context)
    {
        var rule = Rules.FirstOrDefault(candidate => candidate.Matches(context));
        if (rule is not null)
        {
            return rule.Create(context);
        }

        if (context.Source.Equals("hwmon", StringComparison.OrdinalIgnoreCase) &&
            context.Driver.Equals("k10temp", StringComparison.OrdinalIgnoreCase) &&
            K10TempCcdRegex().Match(context.RawLabel) is { Success: true } ccd)
        {
            var number = ccd.Groups["number"].Value;
            return context.Create(
                $"CPU CCD {number} Temperature",
                "Temperature reported for an AMD CPU core complex die.",
                "CPU", "AMD CPU", "Temperature", InterpretationConfidence.Known, "k10temp.tccd");
        }

        if (context.Source.Equals("hwmon", StringComparison.OrdinalIgnoreCase) &&
            context.Driver.Equals("nvme", StringComparison.OrdinalIgnoreCase) &&
            NvmeSensorRegex().Match(context.RawLabel) is { Success: true } nvme)
        {
            var number = nvme.Groups["number"].Value;
            return context.Create(
                $"NVMe Temperature Sensor {number}",
                "Additional vendor-defined NVMe temperature sensor.",
                "Storage", "NVMe", "Temperature", InterpretationConfidence.Generic, "nvme.sensor");
        }

        if (context.Source.Equals("hwmon", StringComparison.OrdinalIgnoreCase) &&
            context.Driver.Equals("coretemp", StringComparison.OrdinalIgnoreCase))
        {
            if (CoreTempPackageRegex().Match(context.RawLabel) is { Success: true } package)
            {
                var number = package.Groups["number"].Value;
                return context.Create(
                    $"CPU Package {number} Temperature",
                    "Intel CPU package temperature reported by the coretemp driver.",
                    "CPU", "Intel CPU", "Temperature", InterpretationConfidence.Known, "coretemp.package");
            }

            if (CoreTempCoreRegex().Match(context.RawLabel) is { Success: true } core)
            {
                var number = core.Groups["number"].Value;
                return context.Create(
                    $"CPU Core {number} Temperature",
                    "Intel CPU core temperature reported by the coretemp driver.",
                    "CPU", "Intel CPU", "Temperature", InterpretationConfidence.Known, "coretemp.core");
            }
        }

        if (context.Source.Equals("hwmon", StringComparison.OrdinalIgnoreCase) &&
            context.Driver.StartsWith("intel_rapl", StringComparison.OrdinalIgnoreCase) &&
            context.Quantity == QuantityKind.Power)
        {
            var label = context.RawLabel.ToLowerInvariant();
            var (name, key) = label switch
            {
                var value when value.Contains("uncore", StringComparison.Ordinal) => ("CPU Uncore Power", "intel_rapl.uncore"),
                var value when value.Contains("dram", StringComparison.Ordinal) => ("DRAM Power", "intel_rapl.dram"),
                var value when value.Contains("core", StringComparison.Ordinal) => ("CPU Core Power", "intel_rapl.core"),
                _ => ("CPU Package Power", "intel_rapl.package")
            };
            return context.Create(
                name,
                "Power domain reading reported by Intel Running Average Power Limit telemetry.",
                "CPU", "Intel CPU", "Power", InterpretationConfidence.Derived, key);
        }

        return null;
    }

    private static SensorPresentation CreateGeneric(PresentationContext context)
    {
        var metric = MetricName(context.Quantity);
        var driver = context.Driver;
        var device = context.DeviceLabel ?? driver switch
        {
            var value when value.StartsWith("iwlwifi", StringComparison.OrdinalIgnoreCase) => "Intel Wi-Fi",
            var value when value.StartsWith("acpitz", StringComparison.OrdinalIgnoreCase) => "ACPI Thermal Zone",
            "amdgpu" => "AMD GPU",
            "k10temp" => "AMD CPU",
            "coretemp" => "Intel CPU",
            "nvme" => "NVMe",
            _ when context.Source.Equals("calculated", StringComparison.OrdinalIgnoreCase) => "Calculated Sensors",
            _ when context.Source.Equals("simulated", StringComparison.OrdinalIgnoreCase) => "Simulated Sensors",
            _ => string.IsNullOrWhiteSpace(driver) ? "Other Sensors" : driver
        };
        var category = driver switch
        {
            "amdgpu" => "GPU",
            "k10temp" or "coretemp" => "CPU",
            "nvme" => "Storage",
            var value when value.StartsWith("iwlwifi", StringComparison.OrdinalIgnoreCase) => "Network",
            var value when value.StartsWith("acpitz", StringComparison.OrdinalIgnoreCase) => "Platform",
            _ when context.Source.Equals("calculated", StringComparison.OrdinalIgnoreCase) => "Calculated",
            _ => "Hardware"
        };

        string displayName;
        string description;
        if (driver.StartsWith("iwlwifi", StringComparison.OrdinalIgnoreCase) && context.Quantity == QuantityKind.Temperature)
        {
            displayName = "Adapter Temperature";
            description = "Temperature reading reported by the Intel Wi-Fi driver.";
        }
        else if (driver.StartsWith("acpitz", StringComparison.OrdinalIgnoreCase) && context.Quantity == QuantityKind.Temperature)
        {
            displayName = "Thermal Zone Temperature";
            description = "Generic temperature reported through an ACPI thermal zone.";
        }
        else
        {
            displayName = context.Measurement.Equals("average", StringComparison.OrdinalIgnoreCase)
                ? $"{context.RawLabel} Average {metric}"
                : context.RawLabel;
            description = $"{metric} reading reported by the {driver} driver.";
        }

        return context.Create(
            displayName,
            description,
            category,
            device,
            metric,
            InterpretationConfidence.Generic,
            null);
    }

    private static string MetricName(QuantityKind quantity) => quantity switch
    {
        QuantityKind.Temperature => "Temperature",
        QuantityKind.TemperatureDelta => "Temperature Delta",
        QuantityKind.Power => "Power",
        QuantityKind.Frequency => "Frequency",
        QuantityKind.Voltage => "Voltage",
        QuantityKind.Current => "Current",
        QuantityKind.RotationalSpeed => "Fan Speed",
        QuantityKind.Percentage => "Percentage",
        QuantityKind.Speed => "Speed",
        QuantityKind.DataSize => "Data Size",
        _ => "Value"
    };

    private static PresentationRule Rule(
        string driver,
        string rawLabel,
        QuantityKind quantity,
        string displayName,
        string description,
        string deviceCategory,
        string deviceDisplayName,
        string metricCategory,
        InterpretationConfidence confidence,
        string documentationKey,
        string? measurement = null) =>
        new("hwmon", driver, rawLabel, quantity, measurement, displayName, description, deviceCategory,
            deviceDisplayName, metricCategory, confidence, documentationKey);

    private sealed record PresentationRule(
        string Source,
        string Driver,
        string RawLabel,
        QuantityKind Quantity,
        string? Measurement,
        string DisplayName,
        string Description,
        string DeviceCategory,
        string DeviceDisplayName,
        string MetricCategory,
        InterpretationConfidence Confidence,
        string DocumentationKey)
    {
        public bool Matches(PresentationContext context) =>
            context.Source.Equals(Source, StringComparison.OrdinalIgnoreCase) &&
            context.Driver.Equals(Driver, StringComparison.OrdinalIgnoreCase) &&
            context.RawLabel.Equals(RawLabel, StringComparison.OrdinalIgnoreCase) &&
            context.Quantity == Quantity &&
            (Measurement is null || context.Measurement.Equals(Measurement, StringComparison.OrdinalIgnoreCase));

        public SensorPresentation Create(PresentationContext context) =>
            context.Create(DisplayName, Description, DeviceCategory, DeviceDisplayName, MetricCategory,
                Confidence, DocumentationKey);
    }

    private sealed record PresentationContext(
        string Source,
        string Driver,
        string? DeviceLabel,
        string DeviceGroupKey,
        string RawLabel,
        QuantityKind Quantity,
        string Measurement)
    {
        public static PresentationContext From(SensorReading reading)
        {
            var driver = Get(reading.Metadata, "driver") ?? reading.Source;
            var deviceKey = Get(reading.Metadata, "deviceKey") ?? driver;
            return new(
                reading.Source,
                driver,
                Get(reading.Metadata, "deviceLabel"),
                $"{reading.Source}:{deviceKey}",
                reading.DisplayName,
                reading.Quantity,
                Get(reading.Metadata, "measurement") ?? "input");
        }

        public SensorPresentation Create(
            string displayName,
            string description,
            string deviceCategory,
            string deviceDisplayName,
            string metricCategory,
            InterpretationConfidence confidence,
            string? documentationKey) =>
            new(
                RawLabel,
                displayName,
                description,
                deviceCategory,
                deviceDisplayName,
                DeviceGroupKey,
                metricCategory,
                confidence,
                documentationKey);

        private static string? Get(IReadOnlyDictionary<string, string> metadata, string key) =>
            metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}
