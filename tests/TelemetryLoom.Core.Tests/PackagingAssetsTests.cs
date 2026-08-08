namespace TelemetryLoom.Core.Tests;

public sealed class PackagingAssetsTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void SystemdUnitUsesUnprivilegedLocalhostServiceContract()
    {
        var unit = Read("packaging", "telemetry-loom.service");
        var settings = Read("src", "TelemetryLoom.Service", "appsettings.json");

        Assert.Contains("After=network.target", unit, StringComparison.Ordinal);
        Assert.Contains("User=telemetry-loom", unit, StringComparison.Ordinal);
        Assert.Contains("Group=telemetry-loom", unit, StringComparison.Ordinal);
        Assert.DoesNotContain("User=root", unit, StringComparison.Ordinal);
        Assert.Contains("WorkingDirectory=/opt/telemetry-loom", unit, StringComparison.Ordinal);
        Assert.Contains("ExecStart=/opt/telemetry-loom/TelemetryLoom.Service", unit, StringComparison.Ordinal);
        Assert.Contains(
            "TelemetryLoom__ConfigPath=/var/lib/telemetry-loom/config.json",
            unit,
            StringComparison.Ordinal);
        Assert.Contains(
            "TelemetryLoom__Access__Mode=LocalOnly",
            unit,
            StringComparison.Ordinal);
        Assert.Contains("TelemetryLoom__Access__ListenAddress=127.0.0.1", unit, StringComparison.Ordinal);
        Assert.Contains("TelemetryLoom__Access__Port=5198", unit, StringComparison.Ordinal);
        Assert.DoesNotContain("Kestrel__Endpoints", unit, StringComparison.Ordinal);
        Assert.Contains("\"Mode\": \"LocalOnly\"", settings, StringComparison.Ordinal);
        Assert.Contains("\"ListenAddress\": \"127.0.0.1\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Kestrel\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", unit, StringComparison.Ordinal);
        Assert.Contains("StateDirectory=telemetry-loom", unit, StringComparison.Ordinal);
        Assert.Contains("ProtectSystem=strict", unit, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerPreservesStateAndInstallsOnlyPackagedApplicationAssets()
    {
        var installer = Read("scripts", "install.sh");

        Assert.Contains("payload_dir=\"${script_dir}/app\"", installer, StringComparison.Ordinal);
        Assert.Contains("state_dir=\"/var/lib/telemetry-loom\"", installer, StringComparison.Ordinal);
        Assert.Contains("install_dir=\"/opt/telemetry-loom\"", installer, StringComparison.Ordinal);
        Assert.Contains("systemctl daemon-reload", installer, StringComparison.Ordinal);
        Assert.Contains("systemctl enable --now", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("config.json\"", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("firewall", installer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseAutomationPackagesBothSupportedSelfContainedRids()
    {
        var workflow = Read(".github", "workflows", "release-packages.yml");
        var packager = Read("scripts", "package-release.sh");
        var validator = Read("scripts", "validate-release-archive.sh");

        Assert.Contains("linux-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("linux-arm64", workflow, StringComparison.Ordinal);
        Assert.Contains("--self-contained true", packager, StringComparison.Ordinal);
        Assert.Contains("gzip -n", packager, StringComparison.Ordinal);
        Assert.Contains("--mode=0755", packager, StringComparison.Ordinal);
        Assert.Contains("telemetry-loom.service", packager, StringComparison.Ordinal);
        Assert.Contains("install.sh", packager, StringComparison.Ordinal);
        Assert.Contains("appsettings\\.Development\\.json", validator, StringComparison.Ordinal);
        Assert.Contains("config\\.json", validator, StringComparison.Ordinal);
        Assert.DoesNotContain("release create", workflow, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine([RepositoryRoot, .. segments]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TelemetryLoom.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the Telemetry Loom repository root.");
    }
}
