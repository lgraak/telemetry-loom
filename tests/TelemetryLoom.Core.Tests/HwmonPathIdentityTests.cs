using TelemetryLoom.Collectors.Linux.Hwmon;

namespace TelemetryLoom.Core.Tests;

public sealed class HwmonPathIdentityTests
{
    [Theory]
    [InlineData("/sys/devices/platform/coretemp.0/hwmon/hwmon3", "platform/coretemp.0")]
    [InlineData("/sys/devices/pci0000:00/0000:00:01.0/0000:01:00.0", "pci0000:00/0000:00:01.0/0000:01:00.0")]
    [InlineData(@"\sys\devices\platform\nct6775.656\hwmon\hwmon9", "platform/nct6775.656")]
    public void NormalizesStableHardwarePath(string input, string expected)
    {
        Assert.Equal(expected, HwmonPathIdentity.NormalizeHardwarePath(input));
    }

    [Fact]
    public void DeviceKeyIgnoresTemporaryHwmonNumber()
    {
        var first = HwmonPathIdentity.DeviceKey("/sys/devices/platform/coretemp.0/hwmon/hwmon3");
        var second = HwmonPathIdentity.DeviceKey("/sys/devices/platform/coretemp.0/hwmon/hwmon9");

        Assert.Equal(first, second);
    }
}
