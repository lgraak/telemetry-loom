using System.Text.Json.Serialization;
using TelemetryLoom.Collectors.Linux.Hwmon;
using TelemetryLoom.Core.Sensors;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSingleton<SimulatedSensorSource>();
builder.Services.AddSingleton<ISensorSource>(services =>
    services.GetRequiredService<SimulatedSensorSource>());

var hwmonRoot = builder.Configuration["Hwmon:RootPath"] ?? SysfsHwmonSnapshotSource.DefaultRootPath;
if (Directory.Exists(hwmonRoot))
{
    builder.Services.AddSingleton<IHwmonSnapshotSource>(new SysfsHwmonSnapshotSource(hwmonRoot));
    builder.Services.AddSingleton<HwmonSensorSource>();
    builder.Services.AddSingleton<ISensorSource>(services =>
        services.GetRequiredService<HwmonSensorSource>());
}

builder.Services.AddSingleton<SensorCatalog>();

var app = builder.Build();

app.MapRazorPages();
app.MapGet("/api/sensors", (SensorCatalog catalog) => catalog.GetSensors());
app.MapGet("/api/sensors/{id}", (string id, SensorCatalog catalog) =>
    catalog.GetSensor(id) is { } sensor ? Results.Ok(sensor) : Results.NotFound());
app.MapGet("/api/status", (SensorCatalog catalog) => Results.Ok(new
{
    service = "telemetry-loom",
    status = "available",
    sensorCount = catalog.GetSensors().Count,
    collectors = catalog.SourceNames
}));

app.Run();
