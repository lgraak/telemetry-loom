namespace TelemetryLoom.Service.Access;

public sealed class AccessOptions
{
    public const string SectionName = "TelemetryLoom:Access";

    public AccessMode Mode { get; set; } = AccessMode.LocalOnly;
    public string ListenAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 5198;
}
