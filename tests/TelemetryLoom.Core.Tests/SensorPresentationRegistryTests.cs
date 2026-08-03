using TelemetryLoom.Collectors.Linux.Hwmon;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Presentation;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Tests;

public sealed class SensorPresentationRegistryTests
{
    [Fact]
    public void EnrichesKnownAmdAndNvmeTermsWithoutChangingStableIds()
    {
        var normalized = LoadCachyOsSource().GetSensors();
        var enriched = normalized.Select(new SensorPresentationRegistry().Enrich).ToArray();

        Assert.Equal(normalized.Select(sensor => sensor.Id), enriched.Select(sensor => sensor.Id));
        AssertPresentation(enriched, "k10temp", "Tctl", "input",
            "CPU Control Temperature", "AMD CPU", InterpretationConfidence.Known, "k10temp.tctl");
        AssertPresentation(enriched, "amdgpu", "sclk", "input",
            "GPU Core Clock", "AMD GPU", InterpretationConfidence.Known, "amdgpu.sclk");
        AssertPresentation(enriched, "amdgpu", "edge", "input",
            "GPU Edge Temperature", "AMD GPU", InterpretationConfidence.Known, "amdgpu.edge");
        AssertPresentation(enriched, "nvme", "Composite", "input",
            "NVMe Composite Temperature", "NVMe", InterpretationConfidence.Known, "nvme.composite");
        AssertPresentation(enriched, "nvme", "Sensor 2", "input",
            "NVMe Temperature Sensor 2", "NVMe", InterpretationConfidence.Generic, "nvme.sensor");
    }

    [Fact]
    public void UsesMeasurementMetadataToDisambiguateDuplicatePptLabels()
    {
        var enriched = LoadCachyOsSource().GetSensors()
            .Select(new SensorPresentationRegistry().Enrich)
            .Where(sensor => sensor.Metadata["driver"] == "amdgpu" && sensor.Presentation?.RawLabel == "PPT")
            .OrderBy(sensor => sensor.Metadata["measurement"], StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(2, enriched.Length);
        Assert.Equal("Average GPU Package Power", enriched[0].DisplayName);
        Assert.Equal("GPU Package Power", enriched[1].DisplayName);
        Assert.NotEqual(enriched[0].Id, enriched[1].Id);
    }

    [Fact]
    public void GenericFallbackKeepsUnknownDriverUsableAndHonest()
    {
        var raw = Reading(
            "fixture:unknown",
            "Board Sensor",
            QuantityKind.Temperature,
            new Dictionary<string, string>
            {
                ["driver"] = "mysterychip",
                ["deviceKey"] = "device-1",
                ["measurement"] = "input"
            });

        var enriched = new SensorPresentationRegistry().Enrich(raw);

        Assert.Equal(raw.Id, enriched.Id);
        Assert.Equal("Board Sensor", enriched.DisplayName);
        Assert.Equal("mysterychip", enriched.Presentation!.DeviceDisplayName);
        Assert.Equal(InterpretationConfidence.Generic, enriched.Presentation.InterpretationConfidence);
        Assert.Null(enriched.Presentation.DocumentationKey);
        Assert.Contains("mysterychip", enriched.Presentation.Description, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Package id 0", "CPU Package 0 Temperature", "coretemp.package")]
    [InlineData("Core 0", "CPU Core 0 Temperature", "coretemp.core")]
    public void SupportsDriverOrientedIntelCoretempMappings(
        string rawLabel,
        string expectedName,
        string documentationKey)
    {
        var enriched = new SensorPresentationRegistry().Enrich(Reading(
            $"fixture:{rawLabel}",
            rawLabel,
            QuantityKind.Temperature,
            new Dictionary<string, string>
            {
                ["driver"] = "coretemp",
                ["deviceKey"] = "intel-package-0",
                ["measurement"] = "input"
            }));

        Assert.Equal(expectedName, enriched.DisplayName);
        Assert.Equal("Intel CPU", enriched.Presentation!.DeviceDisplayName);
        Assert.Equal(documentationKey, enriched.Presentation.DocumentationKey);
    }

    [Fact]
    public void AliasNameOverridesGlossaryNameButKeepsGlossaryHelpAndGrouping()
    {
        var source = LoadCachyOsSource();
        var normalized = new SensorCatalog([source]);
        var target = normalized.GetSensors().Single(sensor =>
            sensor.Metadata["driver"] == "amdgpu" && sensor.DisplayName == "edge");
        var aliases = new SensorAliasRegistry(
            normalized,
            new TelemetryConfigurationRegistry(new MemoryConfigurationStore()));
        aliases.Upsert("gpu.temperature", "Main GPU Temperature", target.Id);
        var enriched = new EnrichedSensorCatalog(normalized, new SensorPresentationRegistry());

        var aliased = new AliasedSensorCatalog(enriched, aliases).GetSensor(target.Id)!;

        Assert.Equal("Main GPU Temperature", aliased.DisplayName);
        Assert.Equal("gpu.temperature", aliased.Alias);
        Assert.Equal("Main GPU Temperature", aliased.Presentation!.DisplayName);
        Assert.Equal("edge", aliased.Presentation.RawLabel);
        Assert.Equal("AMD GPU", aliased.Presentation.DeviceDisplayName);
        Assert.Equal("amdgpu.edge", aliased.Presentation.DocumentationKey);
        Assert.Equal(InterpretationConfidence.UserDefined, aliased.Presentation.InterpretationConfidence);
    }

    [Fact]
    public void EnrichesMustafarFixtureAndKeepsUnknownDriversGeneric()
    {
        var enriched = LoadSource("mustafar-amd-proxmox.json").GetSensors()
            .Select(new SensorPresentationRegistry().Enrich)
            .ToArray();

        AssertPresentation(enriched, "k10temp", "Tccd1", "input",
            "CPU CCD 1 Temperature", "AMD CPU", InterpretationConfidence.Known, "k10temp.tccd");
        Assert.Equal(2, enriched.Count(sensor =>
            sensor.Metadata["driver"] == "nvme" &&
            sensor.Presentation?.RawLabel == "Composite" &&
            sensor.Presentation.DeviceDisplayName == "NVMe"));

        var ethernet = enriched.Single(sensor => sensor.Metadata["driver"].StartsWith("r8169", StringComparison.Ordinal));
        Assert.Equal(InterpretationConfidence.Generic, ethernet.Presentation!.InterpretationConfidence);
        Assert.Null(ethernet.Presentation.DocumentationKey);

        Assert.All(enriched.Where(sensor => sensor.Metadata["driver"] == "spd5118"), sensor =>
        {
            Assert.Equal(InterpretationConfidence.Generic, sensor.Presentation!.InterpretationConfidence);
            Assert.Null(sensor.Presentation.DocumentationKey);
        });
    }

    private static HwmonSensorSource LoadCachyOsSource() =>
        LoadSource("cachyos-amd.json");

    private static HwmonSensorSource LoadSource(string fixtureName) =>
        new(FixtureHwmonSnapshotSource.FromFile(Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            fixtureName)));

    private static SensorReading Reading(
        string id,
        string displayName,
        QuantityKind quantity,
        IReadOnlyDictionary<string, string> metadata) =>
        new(
            id,
            displayName,
            null,
            1,
            quantity,
            UnitCode.Celsius,
            "°C",
            "hwmon",
            SensorStatus.Available,
            DateTimeOffset.Parse("2026-08-03T00:00:00Z"),
            metadata);

    private static void AssertPresentation(
        IReadOnlyList<SensorReading> sensors,
        string driver,
        string rawLabel,
        string measurement,
        string displayName,
        string deviceDisplayName,
        InterpretationConfidence confidence,
        string documentationKey)
    {
        var sensor = sensors.Single(candidate =>
            candidate.Metadata["driver"] == driver &&
            candidate.Presentation?.RawLabel == rawLabel &&
            candidate.Metadata["measurement"] == measurement);
        Assert.Equal(displayName, sensor.DisplayName);
        Assert.Equal(deviceDisplayName, sensor.Presentation!.DeviceDisplayName);
        Assert.Equal(confidence, sensor.Presentation.InterpretationConfidence);
        Assert.Equal(documentationKey, sensor.Presentation.DocumentationKey);
    }

    private sealed class MemoryConfigurationStore : ITelemetryConfigurationStore
    {
        private TelemetryConfigurationDocument _document = new();
        public TelemetryConfigurationDocument Load() => _document;
        public void Save(TelemetryConfigurationDocument document) => _document = document;
    }
}
