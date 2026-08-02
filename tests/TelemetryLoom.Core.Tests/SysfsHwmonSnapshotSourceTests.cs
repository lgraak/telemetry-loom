using TelemetryLoom.Collectors.Linux.Hwmon;

namespace TelemetryLoom.Core.Tests;

public sealed class SysfsHwmonSnapshotSourceTests
{
    [Fact]
    public void CaptureIncludesOnlyRelevantReadOnlySensorAttributes()
    {
        var root = Path.Combine(Path.GetTempPath(), "telemetry-loom-tests", Guid.NewGuid().ToString("N"));
        var devicePath = Path.Combine(root, "hwmon7");
        Directory.CreateDirectory(devicePath);

        try
        {
            File.WriteAllText(Path.Combine(devicePath, "name"), "fixture_driver");
            File.WriteAllText(Path.Combine(devicePath, "label"), "Fixture Device");
            File.WriteAllText(Path.Combine(devicePath, "temp1_input"), "32500");
            File.WriteAllText(Path.Combine(devicePath, "temp1_label"), "Fixture Temperature");
            File.WriteAllText(Path.Combine(devicePath, "pwm1"), "128");
            File.WriteAllText(Path.Combine(devicePath, "serial_number"), "must-not-be-captured");

            var fixture = new SysfsHwmonSnapshotSource(root).Capture();
            var device = Assert.Single(fixture.Devices);

            Assert.Equal(HwmonIdentityQuality.DegradedDriverIdentity, device.IdentityQuality);
            Assert.Equal("unresolved/fixture-driver/fixture-device", device.HardwarePath);
            Assert.Equal(2, device.Attributes.Count);
            Assert.Equal("32500", device.Attributes["temp1_input"]);
            Assert.Equal("Fixture Temperature", device.Attributes["temp1_label"]);
            Assert.DoesNotContain("serial_number", device.Attributes.Keys);
            Assert.DoesNotContain("pwm1", device.Attributes.Keys);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
