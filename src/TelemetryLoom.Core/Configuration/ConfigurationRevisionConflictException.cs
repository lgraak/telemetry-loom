namespace TelemetryLoom.Core.Configuration;

public sealed class ConfigurationRevisionConflictException(long expectedRevision, long actualRevision)
    : InvalidOperationException(
        $"Configuration revision changed from {expectedRevision} to {actualRevision}. Reload and review the latest configuration before saving.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long ActualRevision { get; } = actualRevision;
}
