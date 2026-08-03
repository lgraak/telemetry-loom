using System.Text.Json.Serialization;
using TelemetryLoom.Collectors.Linux.Hwmon;
using TelemetryLoom.Core.Aliases;
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
builder.Services.AddSingleton<IAliasConfigurationStore>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var configuredAliasPath = configuration["TelemetryLoom:ConfigPath"];
    var aliasPath = string.IsNullOrWhiteSpace(configuredAliasPath)
        ? AliasConfigurationPath.GetDefault()
        : configuredAliasPath;
    return new JsonAliasConfigurationStore(aliasPath);
});
builder.Services.AddSingleton<SensorAliasRegistry>();
builder.Services.AddSingleton<AliasedSensorCatalog>();

var app = builder.Build();
_ = app.Services.GetRequiredService<SensorAliasRegistry>();

app.MapRazorPages();
app.MapGet("/api/sensors", (AliasedSensorCatalog catalog) => catalog.GetSensors());
app.MapGet("/api/sensors/by-alias/{key}", (string key, AliasedSensorCatalog catalog) =>
    catalog.GetSensorByAlias(key) is { } sensor ? Results.Ok(sensor) : Results.NotFound());
app.MapGet("/api/sensors/{id}", (string id, AliasedSensorCatalog catalog) =>
    catalog.GetSensor(id) is { } sensor ? Results.Ok(sensor) : Results.NotFound());
app.MapGet("/api/aliases", (SensorAliasRegistry aliases) => aliases.GetAliases());
app.MapGet("/api/aliases/{key}", (string key, SensorAliasRegistry aliases) =>
    aliases.GetAlias(key) is { } alias ? Results.Ok(alias) : Results.NotFound());
app.MapPut("/api/aliases/{key}", UpsertAlias);
app.MapDelete("/api/aliases/{key}", (string key, SensorAliasRegistry aliases) =>
    aliases.Delete(key) ? Results.NoContent() : Results.NotFound());
app.MapGet("/api/status", (AliasedSensorCatalog catalog) => Results.Ok(new
{
    service = "telemetry-loom",
    status = "available",
    sensorCount = catalog.GetSensors().Count,
    collectors = catalog.SourceNames
}));

app.Run();

static IResult UpsertAlias(string key, UpsertAliasRequest request, SensorAliasRegistry aliases)
{
    try
    {
        return Results.Ok(aliases.Upsert(key, request.DisplayName, request.SensorId));
    }
    catch (AliasValidationException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
    catch (AliasSensorNotFoundException exception)
    {
        return Results.NotFound(new ApiError(exception.Message));
    }
    catch (AliasConflictException exception)
    {
        return Results.Conflict(new ApiError(exception.Message));
    }
}

public sealed record UpsertAliasRequest(string DisplayName, string SensorId);
public sealed record ApiError(string Error);
public partial class Program;
