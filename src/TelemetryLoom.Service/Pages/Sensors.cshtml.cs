using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Streaming;
using TelemetryLoom.Core.Sensors;
using TelemetryLoom.Service.LiveUpdates;

namespace TelemetryLoom.Service.Pages;

public sealed class SensorsModel(
    SensorSnapshotPublisher snapshots,
    TelemetrySensorCatalog sensors,
    TimeProvider timeProvider) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Quantity { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Device { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Origin { get; set; }

    public SensorSnapshot Snapshot { get; private set; } = null!;
    public IReadOnlyList<SensorDeviceGroupView> Groups { get; private set; } = [];
    public IReadOnlyList<SensorFilterOption> DeviceOptions { get; private set; } = [];
    public IReadOnlyList<string> StatusOptions { get; } = Enum.GetNames<SensorStatus>();
    public IReadOnlyList<string> QuantityOptions { get; } = Enum.GetNames<QuantityKind>();

    public void OnGet()
    {
        Snapshot = snapshots.GetLatestSnapshot() ?? new SensorSnapshot(
            SensorSnapshot.CurrentSchemaVersion,
            0,
            timeProvider.GetUtcNow(),
            sensors.GetSensors());

        var readings = Snapshot.Sensors.Select(ToView).ToArray();
        DeviceOptions = readings
            .Select(reading => new SensorFilterOption(reading.DeviceKey, reading.DeviceName))
            .DistinctBy(option => option.Value, StringComparer.Ordinal)
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Value, StringComparer.Ordinal)
            .ToArray();
        Groups = readings
            .GroupBy(reading => reading.DeviceKey, StringComparer.Ordinal)
            .Select(group => new SensorDeviceGroupView(
                group.Key,
                group.First().DeviceName,
                group.First().DeviceCategory,
                [.. group
                    .OrderBy(reading => reading.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(reading => reading.Id, StringComparer.Ordinal)]))
            .OrderBy(group => group.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public bool IsVisible(SensorReadingView reading)
    {
        if (!string.IsNullOrWhiteSpace(Q) &&
            !reading.SearchText.Contains(Q.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Matches(Status, reading.Status) &&
               Matches(Quantity, reading.Quantity) &&
               Matches(Device, reading.DeviceKey) &&
               Matches(Origin, reading.Origin);
    }

    private static bool Matches(string? selected, string actual) =>
        string.IsNullOrWhiteSpace(selected) || string.Equals(selected, actual, StringComparison.OrdinalIgnoreCase);

    private static SensorReadingView ToView(SensorReading reading)
    {
        var presentation = reading.Presentation;
        var deviceKey = string.IsNullOrWhiteSpace(presentation?.DeviceGroupKey)
            ? $"{reading.Source}:default"
            : presentation.DeviceGroupKey;
        var deviceName = string.IsNullOrWhiteSpace(presentation?.DeviceDisplayName)
            ? reading.Source
            : presentation.DeviceDisplayName;
        var deviceCategory = string.IsNullOrWhiteSpace(presentation?.DeviceCategory)
            ? "Other"
            : presentation.DeviceCategory;
        var rawLabel = string.IsNullOrWhiteSpace(presentation?.RawLabel)
            ? reading.DisplayName
            : presentation.RawLabel;
        var origin = string.Equals(reading.Source, "calculated", StringComparison.Ordinal) ||
                     reading.Id.StartsWith("calculated:", StringComparison.Ordinal)
            ? "Calculated"
            : "Physical";
        var displayValue = reading.Value is { } value && double.IsFinite(value)
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : "—";
        var searchText = string.Join(" ", new[]
        {
            reading.DisplayName,
            rawLabel,
            reading.Alias,
            reading.Id,
            deviceName,
            deviceKey,
            reading.Source,
            presentation?.Description
        }.Where(value => !string.IsNullOrWhiteSpace(value)));

        return new SensorReadingView(
            reading.Id,
            reading.DisplayName,
            rawLabel,
            presentation?.Description,
            presentation?.InterpretationConfidence.ToString(),
            reading.Alias,
            displayValue,
            reading.UnitSymbol,
            reading.Status.ToString(),
            reading.Quantity.ToString(),
            reading.Source,
            origin,
            reading.LastSuccessfulUpdate,
            deviceKey,
            deviceName,
            deviceCategory,
            searchText);
    }
}

public sealed record SensorFilterOption(string Value, string Label);

public sealed record SensorDeviceGroupView(
    string Key,
    string DisplayName,
    string Category,
    IReadOnlyList<SensorReadingView> Readings);

public sealed record SensorReadingView(
    string Id,
    string DisplayName,
    string RawLabel,
    string? Description,
    string? Confidence,
    string? Alias,
    string DisplayValue,
    string UnitSymbol,
    string Status,
    string Quantity,
    string Source,
    string Origin,
    DateTimeOffset? LastSuccessfulUpdate,
    string DeviceKey,
    string DeviceName,
    string DeviceCategory,
    string SearchText);
