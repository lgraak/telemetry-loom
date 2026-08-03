using System.Runtime.CompilerServices;
using TelemetryLoom.Contracts.Streaming;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Core.Streaming;

public sealed class SensorSnapshotStream(
    TelemetrySensorCatalog sensors,
    TimeProvider timeProvider)
{
    private long _sequence;

    public async IAsyncEnumerable<SensorSnapshot> ReadAllAsync(
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), "Snapshot interval must be positive.");

        using var timer = new PeriodicTimer(interval, timeProvider);
        yield return Capture();
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return Capture();
        }

        SensorSnapshot Capture() =>
            new(
                SensorSnapshot.CurrentSchemaVersion,
                Interlocked.Increment(ref _sequence),
                timeProvider.GetUtcNow(),
                sensors.GetSensors());
    }
}
