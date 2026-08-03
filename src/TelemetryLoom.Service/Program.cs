using System.Text.Json.Serialization;
using TelemetryLoom.Collectors.Linux.Hwmon;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Sensors;
using TelemetryLoom.Service.LiveUpdates;

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
builder.Services.AddSingleton<ITelemetryConfigurationStore>(services =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    var configuredAliasPath = configuration["TelemetryLoom:ConfigPath"];
    var aliasPath = string.IsNullOrWhiteSpace(configuredAliasPath)
        ? AliasConfigurationPath.GetDefault()
        : configuredAliasPath;
    return new JsonTelemetryConfigurationStore(aliasPath);
});
builder.Services.AddSingleton<TelemetryConfigurationRegistry>();
builder.Services.AddSingleton<SensorAliasRegistry>();
builder.Services.AddSingleton<AliasedSensorCatalog>();
builder.Services.AddSingleton<CalculatedSensorRegistry>();
builder.Services.AddSingleton<CalculatedSensorCatalog>();
builder.Services.AddSingleton<TelemetrySensorCatalog>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<LiveUpdateOptions>()
    .Bind(builder.Configuration.GetSection("TelemetryLoom:LiveUpdates"))
    .Validate(
        options => options.IntervalMilliseconds is >= LiveUpdateOptions.MinimumIntervalMilliseconds
            and <= LiveUpdateOptions.MaximumIntervalMilliseconds,
        $"IntervalMilliseconds must be between {LiveUpdateOptions.MinimumIntervalMilliseconds} " +
        $"and {LiveUpdateOptions.MaximumIntervalMilliseconds}.")
    .ValidateOnStart();
builder.Services.AddSingleton<SensorSnapshotPublisher>();
builder.Services.AddHostedService(services =>
    services.GetRequiredService<SensorSnapshotPublisher>());

var app = builder.Build();
_ = app.Services.GetRequiredService<SensorAliasRegistry>();
_ = app.Services.GetRequiredService<CalculatedSensorRegistry>();

app.MapRazorPages();
app.MapGet("/api/sensors", (TelemetrySensorCatalog catalog) => catalog.GetSensors());
app.MapGet("/api/sensors/stream", SensorStreamEndpoint.Stream);
app.MapGet("/api/sensors/by-alias/{key}", (string key, TelemetrySensorCatalog catalog) =>
    catalog.GetSensorByAlias(key) is { } sensor ? Results.Ok(sensor) : Results.NotFound());
app.MapGet("/api/sensors/{id}", (string id, TelemetrySensorCatalog catalog) =>
    catalog.GetSensor(id) is { } sensor ? Results.Ok(sensor) : Results.NotFound());
app.MapGet("/api/aliases", (SensorAliasRegistry aliases) => aliases.GetAliases());
app.MapGet("/api/aliases/{key}", (string key, SensorAliasRegistry aliases) =>
    aliases.GetAlias(key) is { } alias ? Results.Ok(alias) : Results.NotFound());
app.MapPut("/api/aliases/{key}", UpsertAlias);
app.MapDelete("/api/aliases/{key}", (string key, SensorAliasRegistry aliases) =>
    aliases.Delete(key) ? Results.NoContent() : Results.NotFound());
app.MapGet("/api/calculations", (CalculatedSensorRegistry calculations) => calculations.GetDefinitions());
app.MapGet("/api/calculations/{key}", (string key, CalculatedSensorRegistry calculations) =>
    calculations.GetDefinition(key) is { } calculation ? Results.Ok(calculation) : Results.NotFound());
app.MapPut("/api/calculations/{key}", UpsertCalculation);
app.MapDelete("/api/calculations/{key}", (string key, CalculatedSensorRegistry calculations) =>
    calculations.Delete(key) ? Results.NoContent() : Results.NotFound());
app.MapGet("/api/status", (TelemetrySensorCatalog catalog) => Results.Ok(new
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

static IResult UpsertCalculation(string key, UpsertCalculationRequest request, CalculatedSensorRegistry calculations)
{
    try
    {
        return Results.Ok(calculations.Upsert(key, request.DisplayName, request.Formula));
    }
    catch (CalculationValidationException exception)
    {
        return Results.BadRequest(new ApiError(exception.Message));
    }
    catch (CalculationConflictException exception)
    {
        return Results.Conflict(new ApiError(exception.Message));
    }
}

public sealed record UpsertAliasRequest(string DisplayName, string SensorId);
public sealed record UpsertCalculationRequest(string DisplayName, string Formula);
public sealed record ApiError(string Error);
public partial class Program;
