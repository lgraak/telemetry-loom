using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TelemetryLoom.Contracts.Streaming;
using TelemetryLoom.Core.Sensors;

namespace TelemetryLoom.Service.LiveUpdates;

public sealed class SensorSnapshotPublisher(
    TelemetrySensorCatalog sensors,
    TimeProvider timeProvider,
    IOptions<LiveUpdateOptions> options,
    ILogger<SensorSnapshotPublisher> logger) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Channel<SensorSnapshot>> _subscribers = [];
    private SensorSnapshot? _latest;
    private long _sequence;

    public async IAsyncEnumerable<SensorSnapshot> Subscribe(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<SensorSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

        lock (_gate)
        {
            _subscribers.Add(id, channel);
            if (_latest is not null) channel.Writer.TryWrite(_latest);
        }

        try
        {
            await foreach (var snapshot in channel.Reader.ReadAllAsync(cancellationToken))
                yield return snapshot;
        }
        finally
        {
            lock (_gate)
            {
                _subscribers.Remove(id);
                channel.Writer.TryComplete();
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval, timeProvider);
        PublishCurrentSnapshot();
        while (await timer.WaitForNextTickAsync(stoppingToken))
            PublishCurrentSnapshot();
    }

    private void PublishCurrentSnapshot()
    {
        SensorSnapshot snapshot;
        try
        {
            snapshot = new SensorSnapshot(
                SensorSnapshot.CurrentSchemaVersion,
                Interlocked.Increment(ref _sequence),
                timeProvider.GetUtcNow(),
                sensors.GetSensors());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to capture a live sensor snapshot.");
            return;
        }

        lock (_gate)
        {
            _latest = snapshot;
            foreach (var subscriber in _subscribers.Values)
                subscriber.Writer.TryWrite(snapshot);
        }
    }
}
