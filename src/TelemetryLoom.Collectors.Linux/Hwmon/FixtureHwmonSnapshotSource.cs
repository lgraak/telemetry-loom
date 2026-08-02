namespace TelemetryLoom.Collectors.Linux.Hwmon;

public sealed class FixtureHwmonSnapshotSource(HwmonFixture fixture) : IHwmonSnapshotSource
{
    public static FixtureHwmonSnapshotSource FromFile(string path) =>
        new(HwmonFixtureSerializer.Load(path));

    public HwmonFixture Capture() => fixture;
}
