using System.Text.RegularExpressions;

namespace TelemetryLoom.Collectors.Linux.Hwmon;

public sealed partial class SysfsHwmonSnapshotSource(
    string rootPath = SysfsHwmonSnapshotSource.DefaultRootPath,
    TimeProvider? timeProvider = null) : IHwmonSnapshotSource
{
    public const string DefaultRootPath = "/sys/class/hwmon";

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    [GeneratedRegex(@"^(temp|fan|in|curr|power|freq|humidity)\d+_(input|average|label|fault)$")]
    private static partial Regex RelevantAttributeRegex();

    public HwmonFixture Capture()
    {
        var fixture = new HwmonFixture
        {
            CapturedAt = _timeProvider.GetUtcNow(),
            RootPath = rootPath
        };

        if (!Directory.Exists(rootPath))
        {
            return fixture;
        }

        foreach (var directory in Directory.EnumerateDirectories(rootPath, "hwmon*")
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            if (CaptureDevice(directory) is { } device)
            {
                fixture.Devices.Add(device);
            }
        }

        return fixture;
    }

    private static HwmonDeviceSnapshot? CaptureDevice(string directory)
    {
        var driverName = ReadText(Path.Combine(directory, "name"));
        if (string.IsNullOrWhiteSpace(driverName))
        {
            return null;
        }

        var resolvedPath = ResolveDevicePath(directory);
        var identityQuality = resolvedPath is null
            ? HwmonIdentityQuality.DegradedDriverIdentity
            : HwmonIdentityQuality.StableHardwarePath;
        var deviceLabel = ReadText(Path.Combine(directory, "label"));
        var hardwarePath = resolvedPath is null
            ? $"unresolved/{HwmonPathIdentity.Slug(driverName)}/{HwmonPathIdentity.Slug(deviceLabel ?? "device")}"
            : HwmonPathIdentity.NormalizeHardwarePath(resolvedPath);
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            foreach (var path in Directory.EnumerateFiles(directory).OrderBy(path => path, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                if (!RelevantAttributeRegex().IsMatch(name))
                {
                    continue;
                }

                if (ReadText(path) is { } value)
                {
                    attributes[name] = value;
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return new HwmonDeviceSnapshot
        {
            ClassName = Path.GetFileName(directory),
            DriverName = driverName,
            DeviceLabel = deviceLabel,
            HardwarePath = hardwarePath,
            IdentityQuality = identityQuality,
            Attributes = attributes
        };
    }

    private static string? ResolveDevicePath(string directory)
    {
        foreach (var candidate in new[] { Path.Combine(directory, "device"), directory })
        {
            try
            {
                var resolved = new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true);
                if (resolved is not null)
                {
                    return resolved.FullName;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string? ReadText(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
