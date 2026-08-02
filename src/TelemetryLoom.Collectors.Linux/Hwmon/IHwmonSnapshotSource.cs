namespace TelemetryLoom.Collectors.Linux.Hwmon;

public interface IHwmonSnapshotSource
{
    HwmonFixture Capture();
}
