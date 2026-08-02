using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TelemetryLoom.Collectors.Linux.Hwmon;

public static partial class HwmonPathIdentity
{
    [GeneratedRegex(@"/hwmon/hwmon\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex HwmonSuffixRegex();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex SlugCharactersRegex();

    public static string NormalizeHardwarePath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim().TrimEnd('/');
        normalized = HwmonSuffixRegex().Replace(normalized, string.Empty);

        const string sysDevicesPrefix = "/sys/devices/";
        if (normalized.StartsWith(sysDevicesPrefix, StringComparison.Ordinal))
        {
            normalized = normalized[sysDevicesPrefix.Length..];
        }

        return normalized.Trim('/');
    }

    public static string DeviceKey(string hardwarePath)
    {
        var normalized = NormalizeHardwarePath(hardwarePath);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }

    public static string Slug(string value)
    {
        var slug = SlugCharactersRegex().Replace(value.Trim().ToLowerInvariant(), "-").Trim('-');
        return string.IsNullOrEmpty(slug) ? "unknown" : slug;
    }
}
