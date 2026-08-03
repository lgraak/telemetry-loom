namespace TelemetryLoom.Service.LiveUpdates;

public sealed record LiveUpdateOptions
{
    public const int DefaultIntervalMilliseconds = 1000;
    public const int MinimumIntervalMilliseconds = 100;
    public const int MaximumIntervalMilliseconds = 60_000;

    public int IntervalMilliseconds { get; init; } = DefaultIntervalMilliseconds;

    public TimeSpan Interval => TimeSpan.FromMilliseconds(IntervalMilliseconds);
}
