namespace TelemetryLoom.Collectors.Linux.Hwmon;

public sealed record HwmonFixture
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public DateTimeOffset CapturedAt { get; init; }
    public string RootPath { get; init; } = SysfsHwmonSnapshotSource.DefaultRootPath;
    public List<HwmonDeviceSnapshot> Devices { get; init; } = [];
}
