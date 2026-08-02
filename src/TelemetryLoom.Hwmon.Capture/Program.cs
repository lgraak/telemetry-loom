using TelemetryLoom.Collectors.Linux.Hwmon;

var outputPath = args.Length > 0 ? args[0] : "hwmon-fixture.json";
var rootPath = args.Length > 1 ? args[1] : SysfsHwmonSnapshotSource.DefaultRootPath;

if (!Directory.Exists(rootPath))
{
    Console.Error.WriteLine($"hwmon root not found: {rootPath}");
    return 1;
}

var source = new SysfsHwmonSnapshotSource(rootPath);
var fixture = source.Capture();
HwmonFixtureSerializer.Save(outputPath, fixture);

var attributeCount = fixture.Devices.Sum(device => device.Attributes.Count);
Console.WriteLine($"Captured {fixture.Devices.Count} hwmon device(s) and {attributeCount} relevant attribute(s).");
Console.WriteLine($"Review before sharing: {Path.GetFullPath(outputPath)}");
return 0;
