namespace TelemetryLoom.Core.Aliases;

public static class AliasConfigurationPath
{
    public static string GetDefault()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TelemetryLoom",
                "config.json");
        }

        var configurationHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configurationHome))
        {
            configurationHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }

        return Path.Combine(configurationHome, "telemetry-loom", "config.json");
    }
}
