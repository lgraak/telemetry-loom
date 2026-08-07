using Microsoft.AspNetCore.Mvc.RazorPages;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;
using TelemetryLoom.Service.LiveUpdates;

namespace TelemetryLoom.Service.Pages;

public sealed class IndexModel(
    TelemetryConfigurationRegistry configuration,
    SensorSnapshotPublisher snapshots,
    TelemetrySensorCatalog sensors) : PageModel
{
    public int SensorCount { get; private set; }
    public TelemetryConfigurationStatus Configuration { get; private set; } = null!;

    public void OnGet()
    {
        Configuration = configuration.GetStatus();
        SensorCount = snapshots.GetLatestSnapshot()?.Sensors.Count ?? sensors.GetSensors().Count;
    }
}
