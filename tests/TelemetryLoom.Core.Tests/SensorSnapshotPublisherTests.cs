using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TelemetryLoom.Contracts.Sensors;
using TelemetryLoom.Contracts.Streaming;
using TelemetryLoom.Core.Aliases;
using TelemetryLoom.Core.Calculations;
using TelemetryLoom.Core.Configuration;
using TelemetryLoom.Core.Sensors;
using TelemetryLoom.Service.LiveUpdates;

namespace TelemetryLoom.Core.Tests;

public sealed class SensorSnapshotPublisherTests
{
    [Fact]
    public async Task SubscribersReceiveSameImmediateSharedSnapshot()
    {
        var source = new CountingSource();
        using var publisher = CreatePublisher(source, TimeSpan.FromSeconds(30));
        await publisher.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var first = publisher.Subscribe(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        await using var second = publisher.Subscribe(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Assert.True(await first.MoveNextAsync());
        Assert.True(await second.MoveNextAsync());

        Assert.Equal(first.Current.Sequence, second.Current.Sequence);
        Assert.Equal(SensorSnapshot.CurrentSchemaVersion, first.Current.SchemaVersion);
        Assert.Single(first.Current.Sensors);
        Assert.Equal(1, source.ReadCount);
        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PublisherAdvancesSequenceAtConfiguredCadence()
    {
        var source = new CountingSource();
        using var publisher = CreatePublisher(source, TimeSpan.FromMilliseconds(10));
        await publisher.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await using var snapshots = publisher.Subscribe(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

        Assert.True(await snapshots.MoveNextAsync());
        var first = snapshots.Current;
        Assert.True(await snapshots.MoveNextAsync());

        Assert.Equal(first.Sequence + 1, snapshots.Current.Sequence);
        Assert.True(snapshots.Current.CapturedAt >= first.CapturedAt);
        Assert.True(source.ReadCount >= 2);
        await publisher.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task CancellationStopsAWaitingSubscriber()
    {
        using var publisher = CreatePublisher(new CountingSource(), TimeSpan.FromSeconds(30));
        await publisher.StartAsync(CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        await using var snapshots = publisher.Subscribe(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
        Assert.True(await snapshots.MoveNextAsync());

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await snapshots.MoveNextAsync().AsTask());
        await publisher.StopAsync(CancellationToken.None);
    }

    private static SensorSnapshotPublisher CreatePublisher(CountingSource source, TimeSpan interval)
    {
        var sensors = new SensorCatalog([source]);
        var configuration = new TelemetryConfigurationRegistry(new MemoryConfigurationStore());
        var aliases = new SensorAliasRegistry(sensors, configuration);
        var physical = new AliasedSensorCatalog(sensors, aliases);
        var calculations = new CalculatedSensorRegistry(configuration, aliases);
        var calculated = new CalculatedSensorCatalog(physical, calculations);
        return new SensorSnapshotPublisher(
            new TelemetrySensorCatalog(physical, calculated),
            TimeProvider.System,
            Options.Create(new LiveUpdateOptions { IntervalMilliseconds = (int)interval.TotalMilliseconds }),
            NullLogger<SensorSnapshotPublisher>.Instance);
    }

    private sealed class CountingSource : ISensorSource
    {
        private int _readCount;
        public int ReadCount => _readCount;
        public string Name => "fixture";

        public IReadOnlyList<SensorReading> GetSensors()
        {
            var read = Interlocked.Increment(ref _readCount);
            return [CreateReading(read)];
        }

        public SensorReading? GetSensor(string id) => id == "fixture:temperature" ? CreateReading(ReadCount) : null;

        private static SensorReading CreateReading(int value) => new(
            "fixture:temperature", "Temperature", null, value, QuantityKind.Temperature,
            UnitCode.Celsius, "°C", "fixture", SensorStatus.Available,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"), new Dictionary<string, string>());
    }

    private sealed class MemoryConfigurationStore : ITelemetryConfigurationStore
    {
        public TelemetryConfigurationDocument Load() => new();
        public void Save(TelemetryConfigurationDocument document) { }
    }
}
