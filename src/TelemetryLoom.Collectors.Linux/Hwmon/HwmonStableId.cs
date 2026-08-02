namespace TelemetryLoom.Collectors.Linux.Hwmon;

public static class HwmonStableId
{
    public static string Create(
        string driverName,
        string hardwarePath,
        string sensorType,
        int channel,
        string measurement) =>
        $"hwmon:{HwmonPathIdentity.Slug(driverName)}:{HwmonPathIdentity.DeviceKey(hardwarePath)}:" +
        $"{sensorType}:{channel}:{measurement}";
}
