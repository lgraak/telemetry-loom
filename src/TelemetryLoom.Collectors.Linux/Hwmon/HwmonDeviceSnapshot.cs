namespace TelemetryLoom.Collectors.Linux.Hwmon;

public sealed record HwmonDeviceSnapshot
{
    public required string ClassName { get; init; }
    public required string DriverName { get; init; }
    public string? DeviceLabel { get; init; }
    public required string HardwarePath { get; init; }
    public required HwmonIdentityQuality IdentityQuality { get; init; }
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.Ordinal);
}
